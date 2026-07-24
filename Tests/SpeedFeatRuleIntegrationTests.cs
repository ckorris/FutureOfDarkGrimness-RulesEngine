using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 misc - Speed Feat ("once per game, when this unit moves ... you may move +2in on Advance and
    // +4in on Rush/Charge"). Modelled as a single OPTIONAL activated ability at activation start (Cost
    // OncePerGame, gated AllModelsHaveThisRule) that grants a ThisActivation helper rule carrying the
    // movement bonuses. The stage change - a single-ability activation-start rule now prompts Yes/No
    // instead of being force-applied - is exercised by the whole activation suite staying green plus the
    // headless smoke; here the mechanism is proven with the engine shape: offer -> grant -> the moves that
    // result. The app-side SpeedFeatShippedDataTests pin the same over the shipped JSON.
    [TestFixture]
    public class SpeedFeatRuleIntegrationTests
    {
        private const string Boost = "Speed Feat Boost";

        private static SpecialRuleDefinition SpeedFeat() => new("Speed Feat",
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnActivationStart,
                    new Cost.OncePerGame(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    new Effect.AddRule(Boost, ELifetime.ThisActivation),
                    new Condition.AllModelsHaveThisRule(),
                    Label: "Speed Feat"),
            });

        private static SpecialRuleDefinition SpeedFeatBoost() => new(Boost,
            new[]
            {
                new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.Always(),
                    new Effect.MovementBonus(EActionType.Advance, 2f), ELifetime.ThisActivation),
                new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.Always(),
                    new Effect.MovementBonus(EActionType.Rush, 4f), ELifetime.ThisActivation),
                new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.Always(),
                    new Effect.MovementBonus(EActionType.Charge, 4f), ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>());

        private static TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            harness.Register(SpeedFeat());
            harness.Register(SpeedFeatBoost());
            return harness;
        }

        private static float NetMovementBonus(TestRuleHarness harness, IUnit unit, EActionType action)
        {
            var sink = new MovementModifierSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor,
                new MoveActionDeclaredContext(unit, action, BaseDistanceInches: 6f)));
            return sink.Net(action);
        }

        [Test]
        public void SpeedFeat_OffersASingleOptionalAbility_AtActivationStart()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Speed Feat");

            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new ActivationStartContext(unit));

            Assert.That(offers, Has.Count.EqualTo(1),
                "one ability - which is what makes the stage offer it as a Yes/No 'you may use', not a forced pick.");
            Assert.That(offers.Single().RuleName, Is.EqualTo("Speed Feat"));
        }

        [Test]
        public void UsingSpeedFeat_GrantsTheMovementBonus_ForTheActivation()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Speed Feat");

            Assert.That(NetMovementBonus(harness, unit, EActionType.Advance), Is.EqualTo(0f),
                "no bonus before the feat is used.");

            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(unit)).Single();
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, unit));

            Assert.That(NetMovementBonus(harness, unit, EActionType.Advance), Is.EqualTo(2f));
            Assert.That(NetMovementBonus(harness, unit, EActionType.Rush), Is.EqualTo(4f));
            Assert.That(NetMovementBonus(harness, unit, EActionType.Charge), Is.EqualTo(4f));
        }

        [Test]
        public void SpeedFeat_GatesOnEveryModelHavingTheRule()
        {
            TestRuleHarness harness = Harness();
            // A 2-model unit where the rule sits on the unit (both native models have it) offers it; a unit
            // that does not carry it at all offers nothing.
            IUnit withRule = harness.BuildUnit("P1", 2, "Speed Feat");
            IUnit without = harness.BuildUnit("P2", 2);

            Assert.That(harness.OfferAbilities(new ActivationStartContext(withRule)), Has.Count.EqualTo(1));
            Assert.That(harness.OfferAbilities(new ActivationStartContext(without)), Is.Empty);
        }
    }
}
