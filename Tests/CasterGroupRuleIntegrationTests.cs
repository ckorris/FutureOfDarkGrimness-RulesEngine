using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P23's Caster Group (3 refs):
    //
    //   "Pick one model with this rule in this unit to have Caster(X), where X is the total number of models
    //    with this rule in this unit. If the model is killed, pick another to be the new caster, and transfer
    //    all spell tokens to it. The caster loses all unspent spell tokens at the end of the round."
    //
    // Two of those three sentences are already true in this engine and need no code: spell tokens are held
    // by the UNIT, not by a model, so "pick a model to be the caster" and "transfer its tokens when it dies"
    // have no observable content - there is no per-model pool to move. What is left is a per-round pool
    // sized by the LIVING carriers (new ValueSource.RuleCarrierCount) that does NOT carry over (RoundEnd
    // rather than core Caster's ManualOnly).
    //
    // The third thing is the capability. Casting used to be detected by testing for the Caster rule by
    // identity, which Caster Group can never satisfy - its X is a live model count, so it cannot even be
    // granted as a Caster (grants carry no arguments). Both rules now answer the
    // Lifecycle_OnCapabilityQuery question instead, which is what the stages ask.
    [TestFixture]
    public class CasterGroupRuleIntegrationTests
    {
        private const string RuleName = "Caster Group";

        /// <summary>Caster Group as the shipped supplement authors it: the capability, plus a per-round pool
        /// sized by living carriers that is lost at round end.</summary>
        private static SpecialRuleDefinition CasterGroup() => new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                    new Effect.EnableCasting(), ELifetime.UntilEndOfGame),
                new HookEntry(EHookID.Round_OnRoundStart, new Condition.Always(),
                    new Effect.GrantToken(TokenType.SpellTokens, new ValueSource.RuleCarrierCount(),
                        new TokenClearTrigger.RoundEnd()),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>());

        private static TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            harness.Register(CasterGroup());
            harness.Register(CoreRuleCatalog.Caster);
            return harness;
        }

        // --- the capability ---

        [Test]
        public void CasterGroup_ConfersCasting_WithoutBeingTheCasterRule()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 3, RuleName);

            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.True);
            Assert.That(unit.RuleDefinitions.Any(rule => rule.Definition == CoreRuleCatalog.Caster), Is.False,
                "the whole point: it confers casting without BEING Caster, which an identity check missed.");
        }

        [Test]
        public void CoreCaster_StillConfersCasting()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Caster");
            harness.AttachRule(unit, CoreRuleCatalog.Caster, new RuleArgument.Int(2));

            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.True);
        }

        [Test]
        public void AUnitWithNeitherRule_CannotCast()
        {
            TestRuleHarness harness = Harness();

            Assert.That(CapabilityRuleQueries.CanCast(harness.BuildUnit("P1", 3), harness.Evaluator), Is.False);
        }

        [Test]
        public void TheCapabilityIsCarriedByAModel_AsAJoinedCasterHeroWouldBe()
        {
            // #093: the #006 hero-merge relocates a joined hero's own rules onto its MODEL, so a query that
            // only read unit-level rules would report the host unit as unable to cast while still funding
            // its pool at round start (which IS model-aware). The two must agree.
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 3);
            ((ModelData)unit.Models[1]).AttachRuleDefinition(harness.Resolver.Resolve(RuleName));

            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.True);
        }

        [Test]
        public void TheStagesAskForTheCapability_NotForTheCasterRule()
        {
            // SpellTargeting.IsCaster is what ChooseActionStage gates the Cast action on and what
            // CastSpellStage scans for assisters. Pinned separately from CanCast because the tests above
            // call the query DIRECTLY: without this, reverting IsCaster to its old identity check would
            // leave every one of them green while Caster Group silently lost its Cast action.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var resolver = new RuleResolver();
            resolver.Register(CasterGroup());
            var ctx = new TriggeredMoveTestContext(store, new NullPlayerRequester(), ruleResolver: resolver);

            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Psychic Henchmen", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    store.GetDataBinding<ModelData>(store.Create(model)),
                });
            unit.AttachRuleDefinition(resolver.Resolve(RuleName));

            Assert.That(SpellTargeting.IsCaster(ctx, unit), Is.True);
        }

        [Test]
        public void TheCapabilityIsLive_SoAConditionOnTheEntryGatesIt()
        {
            // What an identity check could not express, and what the rest of P23 needs: the casting-support
            // rules offer their service "only if this unit isn't Shaken".
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Conditional Caster",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                        new Condition.Not(new Condition.TokenPresent(TokenType.Shaken)),
                        new Effect.EnableCasting(), ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>()));

            IUnit unit = harness.BuildUnit("P1", 1, "Conditional Caster");
            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.True);

            harness.SeedToken(unit, TokenType.Shaken);
            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.False,
                "the capability is re-answered on every ask, so live state gates it.");
        }

        // --- the pool ---

        [Test]
        public void ThePool_IsSizedByTheNumberOfModelsCarryingTheRule()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 3, RuleName);

            GrantRoundStartTokens(harness, unit);

            Assert.That(unit.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3),
                "'Caster(X), where X is the total number of models with this rule in this unit'.");
        }

        [Test]
        public void ThePoolShrinks_AsCarriersAreKilled()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 4, RuleName);

            Kill(unit.Models[0]);
            Kill(unit.Models[1]);
            GrantRoundStartTokens(harness, unit);

            Assert.That(unit.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "the count is read at each firing, so casualties fund a smaller pool next round.");
        }

        [Test]
        public void OnlyModelsCarryingTheRuleCount()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 4);
            ((ModelData)unit.Models[0]).AttachRuleDefinition(harness.Resolver.Resolve(RuleName));
            ((ModelData)unit.Models[1]).AttachRuleDefinition(harness.Resolver.Resolve(RuleName));

            GrantRoundStartTokens(harness, unit);

            Assert.That(unit.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "two of the four models have it - a mixed unit funds only its carriers.");
        }

        [Test]
        public void UnspentTokens_AreLostAtTheEndOfTheRound()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 3, RuleName);
            GrantRoundStartTokens(harness, unit);

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd,
                new List<ITokenContainer> { unit.Tokens });

            Assert.That(unit.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "'the caster loses all unspent spell tokens at the end of the round' - unlike core " +
                "Caster, whose pool carries over.");
        }

        [Test]
        public void CoreCastersPool_StillCarriesOver()
        {
            // The round-end loss must be Caster Group's alone: both grants land on the same token type, and
            // the clear trigger rides the TOKEN, so mixing them up would quietly nerf every normal caster.
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1);
            harness.AttachRule(unit, CoreRuleCatalog.Caster, new RuleArgument.Int(2));
            GrantRoundStartTokens(harness, unit);

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd,
                new List<ITokenContainer> { unit.Tokens });

            Assert.That(unit.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
        }

        // --- helpers ---

        private static void GrantRoundStartTokens(TestRuleHarness harness, IUnit unit)
        {
            // What StartOfRoundExtraActionStage.GrantRoundStartTokens does, models included (#093).
            OperationApplier.ApplyTokenOperations(harness.Evaluator.Evaluate(
                unit, ERuleSeat.Actor, new RoundStartContext(unit), weapon: null, models: unit.Models));
        }

        private static void Kill(IModel model) => ((ModelData)model).DealWounds(((ModelData)model).TotalWounds);
    }
}
