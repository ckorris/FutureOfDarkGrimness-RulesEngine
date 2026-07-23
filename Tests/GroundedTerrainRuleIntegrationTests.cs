using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 misc - the Grounded family (Grounded Reinforcement / Precision / Stealth, 8 refs), which share
    // one new primitive: Condition.MostModelsWithinInchesOfTerrain, "if a unit ... has most of them within
    // 1in of terrain". Terrain reaches the condition as a hook capability (IHasTerrain) populated from the
    // live layout; the condition reads it against the firing rule's bearer.
    //
    // Two layers, because the condition can be right while the plumbing that feeds it is wrong: the geometry
    // query directly (TerrainProximityQueries), and the condition through the real evaluator + a hit context.
    // The three shipped effects are the same rollModifier family; the app-side GroundedShippedDataTests pin
    // them over the shipped JSON.
    [TestFixture]
    public class GroundedTerrainRuleIntegrationTests
    {
        private const float Within = 1f;

        // A probe with the Grounded shape: ALL models have the rule AND most within 1in of terrain ->
        // Subject-seat -1 to enemy hit (Grounded Stealth's body).
        private static SpecialRuleDefinition Probe() =>
            new("Grounded Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.AllModelsHaveThisRule(),
                            new Condition.MostModelsWithinInchesOfTerrain(Within)),
                        new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack,
                        ERuleSeat.Subject),
                },
                Array.Empty<ActivatedAbility>());

        private static IReadOnlyList<ITerrain> TerrainAt(float x, float z, float radius) =>
            new ITerrain[] { new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(x, z), radius)) };

        private static void Place(IModel model, float x, float z) =>
            ((ModelData)model).SetPosition(new Position(x, z));

        private static void Kill(IModel model) => ((ModelData)model).DealWounds(999f);

        private static int NetEnemyHit(TestRuleHarness harness, IUnit attacker, IUnit defender,
            IReadOnlyList<ITerrain>? terrain)
        {
            var context = new HitRollModifierContext(attacker, defender, DistanceInches: 12f,
                TerrainPieces: terrain);
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject, context));
            return sink.Net(ERollKind.Hit);
        }

        // ---- The condition, through the real evaluator ----------------------------------------------

        [Test]
        public void MostModelsInTerrain_Fires()
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe());
            IUnit defender = harness.BuildUnit("P1", 1, "Grounded Probe"); // model at origin
            IUnit attacker = harness.BuildUnit("P2", 1);

            Assert.That(NetEnemyHit(harness, attacker, defender, TerrainAt(0f, 0f, 3f)), Is.EqualTo(-1),
                "the sole model stands inside the terrain piece.");
        }

        [Test]
        public void NoTerrain_DoesNotFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe());
            IUnit defender = harness.BuildUnit("P1", 1, "Grounded Probe");
            IUnit attacker = harness.BuildUnit("P2", 1);

            Assert.That(NetEnemyHit(harness, attacker, defender, null), Is.EqualTo(0),
                "empty terrain (the AI-valuation / synthetic-hit default) grants nothing.");
        }

        [Test]
        public void TerrainFarFromTheUnit_DoesNotFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe());
            IUnit defender = harness.BuildUnit("P1", 1, "Grounded Probe"); // origin
            IUnit attacker = harness.BuildUnit("P2", 1);

            Assert.That(NetEnemyHit(harness, attacker, defender, TerrainAt(100f, 0f, 2f)), Is.EqualTo(0));
        }

        [Test]
        public void AStrictMajorityIsRequired_NotHalf()
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe());
            IUnit defender = harness.BuildUnit("P1", 4, "Grounded Probe");
            IUnit attacker = harness.BuildUnit("P2", 1);
            IReadOnlyList<ITerrain> terrain = TerrainAt(0f, 0f, 2f);

            List<IModel> models = defender.Models.ToList();
            Place(models[0], 0f, 0f);       // in terrain
            Place(models[1], 0f, 0f);       // in terrain
            Place(models[2], 100f, 0f);     // away
            Place(models[3], 100f, 0f);     // away
            Assert.That(NetEnemyHit(harness, attacker, defender, terrain), Is.EqualTo(0),
                "2 of 4 is not MOST of them.");

            Place(models[2], 0f, 0f);       // now 3 of 4 in terrain
            Assert.That(NetEnemyHit(harness, attacker, defender, terrain), Is.EqualTo(-1));
        }

        [Test]
        public void OnlyLivingModelsCount()
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe());
            IUnit defender = harness.BuildUnit("P1", 3, "Grounded Probe");
            IUnit attacker = harness.BuildUnit("P2", 1);
            IReadOnlyList<ITerrain> terrain = TerrainAt(0f, 0f, 2f);

            List<IModel> models = defender.Models.ToList();
            Place(models[0], 0f, 0f);       // in terrain, but dead
            Place(models[1], 0f, 0f);       // in terrain, but dead
            Place(models[2], 100f, 0f);     // away, alive
            Kill(models[0]);
            Kill(models[1]);

            Assert.That(NetEnemyHit(harness, attacker, defender, terrain), Is.EqualTo(0),
                "counting the dead in-terrain models would wrongly read 2/3 in terrain; only the 1 living " +
                "model counts, and it is away.");
        }

        // ---- The geometry directly: base EDGE, not centre -------------------------------------------

        [Test]
        public void ProximityIsMeasuredFromTheBaseEdge_NotTheCentre()
        {
            var harness = new TestRuleHarness();
            IUnit unit = harness.BuildUnit("P1", 1);
            IModel model = unit.Models.First(); // baseRadius 0.75

            // Zone radius 1 at (0,0). Model centre at x = 2.5: centre is 1.5in from the zone edge (a
            // centre-only test would reject it), but the base edge is only 0.75in from it -> within 1in.
            Place(model, 2.5f, 0f);
            Assert.That(TerrainProximityQueries.MostModelsWithinInches(unit, TerrainAt(0f, 0f, 1f), Within),
                Is.True, "the model's 0.75in base closes the last stretch: base edge 0.75in from terrain.");

            // At x = 3.0 the base edge is 1.25in away -> outside.
            Place(model, 3.0f, 0f);
            Assert.That(TerrainProximityQueries.MostModelsWithinInches(unit, TerrainAt(0f, 0f, 1f), Within),
                Is.False);
        }

        [Test]
        public void EmptyTerrain_IsNeverMost()
        {
            var harness = new TestRuleHarness();
            IUnit unit = harness.BuildUnit("P1", 1);
            Assert.That(TerrainProximityQueries.MostModelsWithinInches(unit, Array.Empty<ITerrain>(), Within),
                Is.False);
        }
    }
}
