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
    // #376 - the AoF Grounded Speed / Grounded Protection pair, which needed no new condition (the
    // #197 Condition.MostModelsWithinInchesOfTerrain serves) but two new capability carriers:
    // MoveActionDeclaredContext and SaveRollCompleteContext now implement IHasTerrain, so the
    // terrain-proximity gate can ride the movement-declare and save-complete hooks. The condition
    // itself is pinned by GroundedTerrainRuleIntegrationTests; these tests pin the two new contexts
    // feeding it - fires with terrain in reach, silent on the empty default - and that the
    // reflection-driven validator now accepts the condition at both hooks.
    [TestFixture]
    public class GroundedSpeedProtectionRuleIntegrationTests
    {
        private const float Within = 1f;

        // Grounded Speed's shape: all models have the rule AND most within 1in of terrain ->
        // Actor-seat movement bonus (+2 Advance; the shipped def adds +4 Rush/Charge entries).
        private static SpecialRuleDefinition SpeedProbe() =>
            new("Grounded Speed Probe",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.And(new Condition.AllModelsHaveThisRule(),
                            new Condition.MostModelsWithinInchesOfTerrain(Within)),
                        new Effect.MovementBonus(EActionType.Advance, 2f), ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>());

        // Grounded Protection's shape: same gate, Subject seat, ignore each wound on 5+.
        private static SpecialRuleDefinition ProtectionProbe() =>
            new("Grounded Protection Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.And(new Condition.AllModelsHaveThisRule(),
                            new Condition.MostModelsWithinInchesOfTerrain(Within)),
                        new Effect.IgnoreWoundOnRoll(5), ELifetime.ThisAttack,
                        ERuleSeat.Subject),
                },
                Array.Empty<ActivatedAbility>());

        private static IReadOnlyList<ITerrain> TerrainAt(float x, float z, float radius) =>
            new ITerrain[] { new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(x, z), radius)) };

        // ---- Movement declare: the new IHasTerrain carrier -----------------------------------------

        private static float NetAdvance(TestRuleHarness harness, IUnit unit,
            IReadOnlyList<ITerrain>? terrain)
        {
            var context = new MoveActionDeclaredContext(unit, EActionType.Advance, 6f, terrain);
            var sink = new MovementModifierSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor, context));
            return sink.Net(EActionType.Advance);
        }

        [Test]
        public void MoveDeclare_MostModelsInTerrain_GetsTheBonus()
        {
            var harness = new TestRuleHarness();
            harness.Register(SpeedProbe());
            IUnit unit = harness.BuildUnit("P1", 1, "Grounded Speed Probe"); // model at origin

            Assert.That(NetAdvance(harness, unit, TerrainAt(0f, 0f, 3f)), Is.EqualTo(2f));
        }

        [Test]
        public void MoveDeclare_EmptyTerrain_GrantsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(SpeedProbe());
            IUnit unit = harness.BuildUnit("P1", 1, "Grounded Speed Probe");

            Assert.That(NetAdvance(harness, unit, null), Is.EqualTo(0f),
                "the null/empty default (query paths without table state) must only omit the bonus.");
        }

        [Test]
        public void MoveDeclare_TerrainFarAway_GrantsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(SpeedProbe());
            IUnit unit = harness.BuildUnit("P1", 1, "Grounded Speed Probe");

            Assert.That(NetAdvance(harness, unit, TerrainAt(100f, 0f, 2f)), Is.EqualTo(0f));
        }

        // ---- Save complete: the new IHasTerrain carrier --------------------------------------------

        private static WoundIgnoreSink IgnoreAfterSaves(TestRuleHarness harness, IUnit attacker,
            IUnit defender, IReadOnlyList<ITerrain>? terrain)
        {
            var context = new SaveRollCompleteContext(attacker, defender,
                TestDice.Faces(1, 2, 3), TerrainPieces: terrain);
            var sink = new WoundIgnoreSink();
            sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject, context));
            return sink;
        }

        [Test]
        public void SaveComplete_MostModelsInTerrain_IgnoresOnFivePlus()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProtectionProbe());
            IUnit defender = harness.BuildUnit("P1", 1, "Grounded Protection Probe"); // origin
            IUnit attacker = harness.BuildUnit("P2", 1);

            WoundIgnoreSink sink = IgnoreAfterSaves(harness, attacker, defender, TerrainAt(0f, 0f, 3f));
            Assert.That(sink.HasIgnore, Is.True);
            Assert.That(sink.Threshold, Is.EqualTo(5));
        }

        [Test]
        public void SaveComplete_EmptyTerrain_DoesNotIgnore()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProtectionProbe());
            IUnit defender = harness.BuildUnit("P1", 1, "Grounded Protection Probe");
            IUnit attacker = harness.BuildUnit("P2", 1);

            Assert.That(IgnoreAfterSaves(harness, attacker, defender, null).HasIgnore, Is.False,
                "the empty default (AI volley valuation) must only omit the protection.");
        }

        // ---- The reflection-driven validator now accepts the condition at both hooks ---------------

        [Test]
        public void Validator_AcceptsTerrainCondition_AtBothNewHooks()
        {
            var validator = new RuleValidator();
            Assert.That(validator.Validate(SpeedProbe()), Is.Empty,
                "MoveActionDeclaredContext must advertise IHasTerrain to the capability check.");
            Assert.That(validator.Validate(ProtectionProbe()), Is.Empty,
                "SaveRollCompleteContext must advertise IHasTerrain to the capability check.");
        }
    }
}
