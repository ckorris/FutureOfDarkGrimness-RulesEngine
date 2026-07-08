using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #093 (defensive "all-models" rules). Stealth/Regeneration/Fearless
    // are unit-wide rules the rulebook says apply only if EVERY model in the unit has them. They now carry
    // Condition.AllModelsHaveThisRule, so a mixed unit — a joined hero that doesn't natively have the rule —
    // no longer benefits, while a homogeneous unit (and a unit whose hero also has the rule) still does.
    // Exercises the real CoreRuleCatalog.Stealth definition through the RuleEvaluator, mirroring
    // SpecialRuleTests' HitRollModifierContext assertion and HeroPerModelRuleIntegrationTests' merge harness.
    [TestFixture]
    public class AllModelsRuleGateIntegrationTests
    {
        private static readonly Position Pos = new Position(0, 5);

        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(1));
        }

        [Test]
        public void Stealth_HomogeneousUnit_AllModelsHaveIt_Applies()
        {
            UnitData defender = MakeUnit(modelCount: 5);
            defender.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

            Assert.That(StealthApplies(defender), Is.True,
                "every model of a plain Stealth unit has the rule, so the -1-to-hit applies.");
        }

        [Test]
        public void Stealth_JoinedHeroLacksRule_Suppressed()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);

            Assert.That(StealthApplies(defender), Is.False,
                "a joined hero without Stealth breaks it for the whole unit — the -1 no longer applies.");
        }

        [Test]
        public void Stealth_JoinedHeroAlsoHasRule_Applies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: true);

            Assert.That(StealthApplies(defender), Is.True,
                "when the hero also carries Stealth, every living model has it, so the -1 applies.");
        }

        [Test]
        public void Stealth_DeadHeroWithoutRule_AppliesToSurvivors()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);
            IModel hero = defender.Models.First(m => m.ID == defender.JoinedHeroModelId!.Value);
            hero.DealWounds(hero.TotalWounds); // the hero falls — only native Stealth grunts remain

            Assert.That(StealthApplies(defender), Is.True,
                "the all-models check ignores dead models, so losing the ruleless hero restores Stealth.");
        }

        // #183 — unit-held grants cover the joined hero too: a buff cast on the combined unit (or an aura)
        // targets every current model, so it must not be broken by the hero the way a host STATIC rule is.
        [Test]
        public void Stealth_GrantedToUnit_CoversJoinedHero_Applies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);
            GrantStealth(defender);

            Assert.That(StealthApplies(defender), Is.True,
                "a unit-held Stealth grant covers the joined hero, so the gate passes despite the hero " +
                "lacking the rule statically.");
        }

        // #183 — the aura-hero scenario: NOBODY has Stealth statically; the whole unit (hero included) has
        // it only via a RuleGrant token, the way an aura the hero itself brought would grant it. The granted
        // rule must both be collected (resolver) and pass its own all-models gate.
        [Test]
        public void Stealth_PureGrant_NoStaticCopyAnywhere_Applies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false, hostHasStealth: false);
            GrantStealth(defender);

            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Stealth);
            var evaluator = new RuleEvaluator(new FixedDiceRoller(1), null, resolver);

            Assert.That(StealthApplies(defender, evaluator), Is.True,
                "a granted-only Stealth applies to a hero-joined unit: the grant covers every living model.");
        }

        // --- harness ---

        // True if CoreRuleCatalog.Stealth produces its -1-to-hit modifier for this defender at > 9".
        private bool StealthApplies(UnitData defender, RuleEvaluator? evaluator = null)
        {
            UnitData attacker = MakeUnit(modelCount: 1);
            var context = new HitRollModifierContext(attacker, defender, DistanceInches: 12f);
            IReadOnlyList<RuleOperation> ops =
                (evaluator ?? _evaluator).Evaluate(defender, ERuleSeat.Subject, context);
            return ops.OfType<RuleOperation.ApplyRollModifier>()
                .Any(op => op.Roll == ERollKind.Hit && op.Delta == -1);
        }

        private static void GrantStealth(UnitData unit) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Stealth", ELifetime.UntilEndOfGame)));

        // A Stealth host unit (Stealth on the unit) merged with a 1-model hero that either does or doesn't
        // carry Stealth on its own. Mirrors HeroPerModelRuleIntegrationTests' merge harness.
        private UnitData MakeStealthUnitWithHero(bool heroHasStealth, bool hostHasStealth = true)
        {
            UnitData host = MakeUnit(modelCount: 3);
            if (hostHasStealth)
            {
                host.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            }

            UnitData hero = MakeUnit(modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroHasStealth)
            {
                hero.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            }

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            return _store.GetDataBinding<UnitData>(_store.Create(host)).GetValue();
        }

        private UnitData MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), Pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
        }
    }
}
