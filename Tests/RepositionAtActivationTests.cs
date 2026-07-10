using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 reposition-at-activation: "When this unit is activated, you may place all models with this rule in
    // it anywhere fully within D3in of their position." (Wolfborn and Rapid Blink, word for word; Bounding is
    // D3+1in; Rapid Blink Boost widens D3 to 2D3.)
    //
    // A PLACEMENT, not a move (owner's ruling): nothing is asked of the path, only of the destination. So it
    // rides PlaceObjectsRequest with the new per-model MaxDistanceFromStartInches radius, rather than
    // InvokeTriggeredMove.
    [TestFixture]
    public class RepositionAtActivationTests
    {
        private static SpecialRuleDefinition Reposition(string name, DiceExpression die, int plus = 0) =>
            new(name,
                new[]
                {
                    new HookEntry(EHookID.Activation_OnActivationStart,
                        new Condition.Always(),
                        new Effect.RepositionAtActivation(die, plus),
                        ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>());

        private static float RepositionInches(TestRuleHarness harness, IUnit unit) =>
            harness.Evaluate(unit, ERuleSeat.Actor, new ActivationStartContext(unit))
                .OfType<RuleOperation.RepositionModels>()
                .Sum(op => op.MaxInches);

        [Test]
        public void ADiceRoll_BecomesAConcreteDistance_OnTheOperation()
        {
            // FixedDiceRoller(3) makes the D3 land on 3.
            var harness = new TestRuleHarness(new FixedDiceRoller(3));
            harness.Register(Reposition("Wolfborn", new DiceExpression.D3()));
            IUnit unit = harness.BuildUnit("P1", 1, "Wolfborn");

            Assert.That(RepositionInches(harness, unit), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void BoundingAddsItsFlatInch_OnTopOfTheRolledDie()
        {
            var harness = new TestRuleHarness(new FixedDiceRoller(2));
            harness.Register(Reposition("Bounding", new DiceExpression.D3(), plus: 1));
            IUnit unit = harness.BuildUnit("P1", 1, "Bounding");

            Assert.That(RepositionInches(harness, unit), Is.EqualTo(3f).Within(0.001f),
                "Bounding is D3+1in, so a rolled 2 gives 3in.");
        }

        [Test]
        public void RapidBlinkBoost_ComposesToTwoDice_AsAnIncrement_NotASecondPrompt()
        {
            // The Boost discipline (#196): a Boost supplies the INCREMENT. "Within 2D3in instead of D3in" is
            // the base's D3 plus one more D3. Both entries fire; the stage sums them into ONE placement.
            var harness = new TestRuleHarness(new FixedDiceRoller(3));
            harness.Register(Reposition("Rapid Blink", new DiceExpression.D3()));
            harness.Register(Reposition("Rapid Blink Boost", new DiceExpression.D3()));
            IUnit unit = harness.BuildUnit("P1", 1, "Rapid Blink", "Rapid Blink Boost");

            IReadOnlyList<RuleOperation> ops =
                harness.Evaluate(unit, ERuleSeat.Actor, new ActivationStartContext(unit));

            Assert.That(ops.OfType<RuleOperation.RepositionModels>().Count(), Is.EqualTo(2),
                "Two rules, two operations - which the stage folds, rather than prompting twice.");
            Assert.That(ops.OfType<RuleOperation.RepositionModels>().Sum(op => op.MaxInches),
                Is.EqualTo(6f).Within(0.001f), "2D3 with both dice showing 3.");
        }

        [Test]
        public void TheBaseAlone_IsNotBoosted()
        {
            var harness = new TestRuleHarness(new FixedDiceRoller(3));
            harness.Register(Reposition("Rapid Blink", new DiceExpression.D3()));
            IUnit unit = harness.BuildUnit("P1", 1, "Rapid Blink");

            Assert.That(RepositionInches(harness, unit), Is.EqualTo(3f).Within(0.001f));
        }

        // ---- the per-model radius constraint the placement rides on -----------------------------------

        [Test]
        public void APlacementWithinTheRadius_IsAccepted_AndBeyondItIsNot()
        {
            var start = new Position(10f, 10f);

            Assert.That(PlacementUtilities.IsWithinStartRadius(new Position(12f, 10f), start, 3f), Is.True);
            Assert.That(PlacementUtilities.IsWithinStartRadius(new Position(13f, 10f), start, 3f), Is.True,
                "Exactly on the boundary is legal; the epsilon absorbs pixel-to-inch rounding.");
            Assert.That(PlacementUtilities.IsWithinStartRadius(new Position(13.5f, 10f), start, 3f), Is.False);
        }

        [Test]
        public void AZeroRadius_MeansUnconstrained_SoDeploymentIsUnaffected()
        {
            Assert.That(PlacementUtilities.IsWithinStartRadius(
                new Position(40f, 40f), new Position(0f, 0f), 0f), Is.True,
                "Every deployment and reserve arrival passes 0 here and must not be constrained.");
        }

        [Test]
        public void StandingStill_IsAlwaysInsideTheRadius()
        {
            // Which is why declining ("you MAY place") is always available, and is the CLI/AI default.
            var start = new Position(7.25f, 3.5f);
            Assert.That(PlacementUtilities.IsWithinStartRadius(start, start, 1f), Is.True);
        }

        [Test]
        public void TheRequest_CarriesTheRadius_AndDefaultsToUnconstrained()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(), store);
            var binding = store.GetDataBinding<ModelData>(store.Create(model));
            var zone = new RectangularZone(0f, 60f, 0f, 44f);

            var reposition = new PlaceObjectsRequest<ModelData>(player, "Reposition", zone,
                new[] { binding }, allowCancel: true, maxDistanceFromStartInches: 2.5f);
            var deployment = new PlaceObjectsRequest<ModelData>(player, "Deploy", zone, new[] { binding });

            Assert.That(reposition.MaxDistanceFromStartInches, Is.EqualTo(2.5f));
            Assert.That(reposition.AllowCancel, Is.True, "'You may place' - declining is legal.");
            Assert.That(deployment.MaxDistanceFromStartInches, Is.EqualTo(0f));
        }
    }
}
