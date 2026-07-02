using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #093 (per-model activated abilities). GatherOffers now reads each
    // living model's per-model rules, so a joined hero's activated ability (Vanguard, Martial Prowess) is
    // offered for the host unit even though the merge relocated it onto the hero MODEL. Uses a trivial
    // "Cast" ability at Activation_OnActionChoice (as CustomActionRuleIntegrationTests does) through the real
    // RuleEvaluator.GatherOffers + the hero merge harness.
    [TestFixture]
    public class PerModelActivatedAbilityTests
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
        public void JoinedHeroActivatedAbility_IsOffered()
        {
            UnitData host = MakeHostWithHero(heroAbility: MakeCastRule(), unitAbility: null);

            var offers = _evaluator.GatherOffers(new ActionChoiceContext(host));

            offers.HasOffer("Cast");
        }

        [Test]
        public void PlainUnit_NoActivatedAbility_NotOffered()
        {
            UnitData host = MakeHostWithHero(heroAbility: null, unitAbility: null);

            var offers = _evaluator.GatherOffers(new ActionChoiceContext(host));

            Assert.That(offers, Is.Empty, "a unit whose hero carries no activated ability offers nothing.");
        }

        [Test]
        public void DeadHero_ActivatedAbility_NotOffered()
        {
            UnitData host = MakeHostWithHero(heroAbility: MakeCastRule(), unitAbility: null);
            IModel hero = host.Models.First(m => m.ID == host.JoinedHeroModelId!.Value);
            hero.DealWounds(hero.TotalWounds);

            var offers = _evaluator.GatherOffers(new ActionChoiceContext(host));

            Assert.That(offers.Any(o => o.RuleName == "Cast"), Is.False,
                "a dead hero contributes no activated abilities.");
        }

        [Test]
        public void AbilityOnUnitAndHero_OfferedOnce()
        {
            // The SAME rule definition on both the unit and the hero (via the merge) must offer once, not
            // twice — the no-stack rule for the AnyOwner union.
            SpecialRuleDefinition cast = MakeCastRule();
            UnitData host = MakeHostWithHero(heroAbility: cast, unitAbility: cast);

            var offers = _evaluator.GatherOffers(new ActionChoiceContext(host));

            Assert.That(offers.Count(o => o.RuleName == "Cast"), Is.EqualTo(1),
                "an ability carried by both the unit and the hero is offered once.");
        }

        // --- harness ---

        private UnitData MakeHostWithHero(SpecialRuleDefinition? heroAbility, SpecialRuleDefinition? unitAbility)
        {
            UnitData host = MakeUnit(modelCount: 3);
            if (unitAbility != null)
            {
                host.AttachRuleDefinition(new ResolvedRule(unitAbility.Name, unitAbility));
            }

            UnitData hero = MakeUnit(modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroAbility != null)
            {
                hero.AttachRuleDefinition(new ResolvedRule(heroAbility.Name, heroAbility));
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
            return new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
        }

        // A trivial "Cast" activated ability at the action-choice hook (mirrors CustomActionRuleIntegrationTests).
        private static SpecialRuleDefinition MakeCastRule()
        {
            var ability = new ActivatedAbility(
                TriggerHook: EHookID.Activation_OnActionChoice,
                Cost: new Cost.OncePerActivation(),
                TargetSelector: new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                Effect: new Effect.GrantToken(new TokenType("PerModelAbilityFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                AvailableWhen: new Condition.Always());
            return new SpecialRuleDefinition("Cast", Array.Empty<HookEntry>(), new[] { ability });
        }
    }
}
