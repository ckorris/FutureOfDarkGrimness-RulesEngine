using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 — the "shoots or charges enemies over 9in away" gate that six corpus Boost rules share
    // (Devout / Ferocious / Warbound / Infected / Mischievous / Scrapper Boost).
    //
    // Condition.DistanceGreaterThan reads the LIVE attacker-to-target distance. A melee attack is resolved
    // in base contact (MELEE_RANGE_INCHES_HORIZONTAL == 2), so a live-distance gate of 9in can never pass
    // in melee — which silently disabled the melee half of all six rules. Condition.AttackedFromOverInches
    // reads the distance the attack was LAUNCHED from instead: the live distance when shooting, and the
    // distance to the defender at activation start when charging (this engine models Charge as the melee
    // attack, with the approach a separate Move, so activation start is where the charge was declared).
    //
    // Two layers are asserted here, because the condition can be right while the plumbing that feeds it is
    // wrong: the condition against hand-built contexts, and the real charge path that fills those contexts.
    [TestFixture]
    public class AttackOriginDistanceTests
    {
        // ---- The condition, against hand-built contexts ----------------------------------------------

        private static SpecialRuleDefinition ProbeAtHitComplete(Condition gate) =>
            new("Boost",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, gate,
                        new Effect.RollModifier(ERollKind.Save, -2), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>());

        private static SpecialRuleDefinition ProbeAtSaveComplete(Condition gate) =>
            new("SaveBoost",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, gate,
                        new Effect.AddExtraWound(OnRollValue: 1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>());

        private static bool FiredRollModifier(IReadOnlyList<RuleOperation> ops) =>
            ops.OfType<RuleOperation.ApplyRollModifier>().Any();

        private static bool FiredExtraWound(IReadOnlyList<RuleOperation> ops) =>
            ops.OfType<RuleOperation.InsertExtraWounds>().Any();

        [Test]
        public void Shooting_FromBeyondTheThreshold_Fires()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            var context = new HitRollCompleteContext(attacker, target, TestDice.Faces(4),
                DistanceInches: 12f, IsMelee: false);

            Assert.That(FiredRollModifier(harness.Evaluate(attacker, ERuleSeat.Actor, context)), Is.True);
        }

        [Test]
        public void Shooting_FromInsideTheThreshold_DoesNotFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            var context = new HitRollCompleteContext(attacker, target, TestDice.Faces(4),
                DistanceInches: 6f, IsMelee: false);

            Assert.That(FiredRollModifier(harness.Evaluate(attacker, ERuleSeat.Actor, context)), Is.False);
        }

        [Test]
        public void Melee_ChargeDeclaredFromBeyondTheThreshold_Fires()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            // Live distance is base contact, as every melee resolution is. Only the launch distance is > 9.
            var context = new HitRollCompleteContext(attacker, target, TestDice.Faces(4),
                DistanceInches: 0.5f, IsMelee: true, IsCharging: true, IsSpell: false,
                ChargeOriginDistanceInches: 11f);

            Assert.That(FiredRollModifier(harness.Evaluate(attacker, ERuleSeat.Actor, context)), Is.True,
                "A charge launched from over 9in away must satisfy the gate even though the swing lands in contact.");
        }

        [Test]
        public void Melee_ChargeDeclaredFromInsideTheThreshold_DoesNotFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            var context = new HitRollCompleteContext(attacker, target, TestDice.Faces(4),
                DistanceInches: 0.5f, IsMelee: true, IsCharging: true, IsSpell: false,
                ChargeOriginDistanceInches: 4f);

            Assert.That(FiredRollModifier(harness.Evaluate(attacker, ERuleSeat.Actor, context)), Is.False,
                "A short charge must not get the bonus - this is what 'any charge counts' would have broken.");
        }

        [Test]
        public void Melee_StrikeBack_NeverFires()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            // A strike-back was launched from nowhere: isCharging false, so no origin distance is recorded.
            var context = new HitRollCompleteContext(attacker, target, TestDice.Faces(4),
                DistanceInches: 0.5f, IsMelee: true, IsCharging: false);

            Assert.That(FiredRollModifier(harness.Evaluate(attacker, ERuleSeat.Actor, context)), Is.False);
        }

        [Test]
        public void SaveRollHook_ReadsTheSameGate_ForShootingAndForCharging()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtSaveComplete(new Condition.AttackedFromOverInches(9f)));

            IUnit attacker = harness.BuildUnit("P1", 1, "SaveBoost");
            IUnit defender = harness.BuildUnit("P2", 1);

            var farShot = new SaveRollCompleteContext(attacker, defender, TestDice.Faces(1),
                IsMelee: false, IsSpell: false, DistanceInches: 12f);
            var nearShot = new SaveRollCompleteContext(attacker, defender, TestDice.Faces(1),
                IsMelee: false, IsSpell: false, DistanceInches: 3f);
            var longCharge = new SaveRollCompleteContext(attacker, defender, TestDice.Faces(1),
                IsMelee: true, IsSpell: false, DistanceInches: 0.5f, ChargeOriginDistanceInches: 11f);
            var shortCharge = new SaveRollCompleteContext(attacker, defender, TestDice.Faces(1),
                IsMelee: true, IsSpell: false, DistanceInches: 0.5f, ChargeOriginDistanceInches: 2f);

            Assert.That(FiredExtraWound(harness.Evaluate(attacker, ERuleSeat.Actor, farShot)), Is.True);
            Assert.That(FiredExtraWound(harness.Evaluate(attacker, ERuleSeat.Actor, nearShot)), Is.False);
            Assert.That(FiredExtraWound(harness.Evaluate(attacker, ERuleSeat.Actor, longCharge)), Is.True,
                "The wound-side Boost rules gate on the same launch distance as the hit-side ones.");
            Assert.That(FiredExtraWound(harness.Evaluate(attacker, ERuleSeat.Actor, shortCharge)), Is.False);
        }

        // ---- The plumbing that fills those contexts --------------------------------------------------

        [Test]
        public void ActivationStart_SnapshotsDistanceToEveryEnemy_BeforeTheUnitMoves()
        {
            World world = World.Build(attackerAt: new Position(0, 0), defenderAt: new Position(20, 0));

            world.UnitContext.Reset(world.Attacker);

            Assert.That(world.UnitContext.TryGetActivationStartDistanceTo(
                world.Defender.GetValue().ID, out float distance), Is.True);
            // 20in centre-to-centre, minus the two 0.5in base radii.
            Assert.That(distance, Is.EqualTo(19f).Within(0.01f));
        }

        [Test]
        public void ChargeOriginDistance_SurvivesTheMoveIntoContact()
        {
            World world = World.Build(attackerAt: new Position(0, 0), defenderAt: new Position(20, 0));
            world.UnitContext.Reset(world.Attacker);

            // The charge happens: the attacker closes to base contact. The live distance is now ~0, and the
            // pre-move geometry is gone - only the activation-start snapshot remembers it.
            world.MoveAttackerTo(new Position(1, 0));

            ICombatMetadata metadata = world.ConsumeChargeAttack();

            Assert.That(metadata.IsCharging, Is.True);
            Assert.That(metadata.ChargeOriginDistanceInches, Is.EqualTo(19f).Within(0.01f),
                "The metadata must carry where the charge was declared from, not where the swing lands.");
        }

        [Test]
        public void ChargeOriginDistance_IsZero_WhenNotCharging()
        {
            World world = World.Build(attackerAt: new Position(0, 0), defenderAt: new Position(20, 0));
            world.UnitContext.Reset(world.Attacker);
            world.MoveAttackerTo(new Position(1, 0));

            ICombatMetadata metadata = world.ConsumeAttack(isCharging: false);

            Assert.That(metadata.ChargeOriginDistanceInches, Is.EqualTo(0f),
                "A strike-back has no launch distance; it must not inherit the charger's.");
        }

        [Test]
        public void ChargeOriginDistance_IsZero_ForAShortCharge()
        {
            World world = World.Build(attackerAt: new Position(0, 0), defenderAt: new Position(4, 0));
            world.UnitContext.Reset(world.Attacker);
            world.MoveAttackerTo(new Position(1, 0));

            ICombatMetadata metadata = world.ConsumeChargeAttack();

            Assert.That(metadata.ChargeOriginDistanceInches, Is.EqualTo(3f).Within(0.01f));
            Assert.That(metadata.ChargeOriginDistanceInches, Is.LessThan(9f),
                "Sanity: a 3in charge must fall on the wrong side of a 9in gate.");
        }

        /// <summary>Two single-model armies on opposing teams, plus the activation context under test.</summary>
        private sealed class World
        {
            public GameDataStore Store = null!;
            public TestGameContext Context = null!;
            public DataBinding<UnitData> Attacker = null!;
            public DataBinding<UnitData> Defender = null!;
            public UnitActionContext UnitContext = null!;

            public static World Build(Position attackerAt, Position defenderAt)
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var context = new TestGameContext(store, new FixedDiceRoller(4));

                var p1 = new PlayerID(Guid.NewGuid());
                var p2 = new PlayerID(Guid.NewGuid());

                DataBinding<UnitData> attacker = MakeUnit(store, p1, attackerAt);
                DataBinding<UnitData> defender = MakeUnit(store, p2, defenderAt);

                store.Create(new ArmyData(p1, new List<DataBinding<UnitData>> { attacker }));
                store.Create(new ArmyData(p2, new List<DataBinding<UnitData>> { defender }));
                store.Create(new TeamData(0, new List<PlayerID> { p1 }));
                store.Create(new TeamData(1, new List<PlayerID> { p2 }));

                return new World
                {
                    Store = store,
                    Context = context,
                    Attacker = attacker,
                    Defender = defender,
                    UnitContext = new UnitActionContext(context, attacker),
                };
            }

            public void MoveAttackerTo(Position position)
            {
                Attacker.GetValue().ModelBindings[0].GetValue().SetPosition(position);
            }

            public ICombatMetadata ConsumeChargeAttack() => ConsumeAttack(isCharging: true);

            public ICombatMetadata ConsumeAttack(bool isCharging)
            {
                var combat = new CombatActionContext(Context, Attacker, isMelee: true, attackerMoved: true,
                    isCharging: isCharging, activationContext: UnitContext);
                combat.SetDefender(Defender);
                // SetAttackWeapon keys the available-weapon pool by reference, so hand it the unit's own instance.
                combat.SetAttackWeapon(Attacker.GetValue().GetMeleeWeapons().Single(), out _);
                return combat.ConsumeAttackIntoContext(Context);
            }

            private static Weapon MeleeWeapon() =>
                new Weapon("Claws", rangeInches: 0f, attacks: 1, armorPenetration: 0);

            private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID player, Position at)
            {
                var model = new ModelData(baseRadiusInches: 0.5f,
                    weapons: new List<Weapon> { MeleeWeapon() },
                    initialPosition: at, gameDataStore: store);
                DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

                var unit = new UnitData(player, "Test Unit", quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>> { modelBinding });
                return store.GetDataBinding<UnitData>(store.Create(unit));
            }
        }
    }
}
