using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #206 — forced-charge ("must engage, no Pass") is a PROXIMITY obligation, evaluated fresh at Choose
    // Action, not a distance-moved one. A unit within ENEMY_STANDOFF_DISTANCE_INCHES (1", base-to-base) of an
    // enemy can't idle in its face - Pass is gated. The wider melee/charge band (2") is not forced: a unit at
    // 1"-2" may Charge but may also Pass. Distance moved no longer gates Pass at all (the move validator lets a
    // unit end right up against an enemy; this is where the consequence lives). Allied units never force a charge.
    //
    // Models are radius 0.5", so base-to-base gap = centre distance - 1.0". Enemy at centre 1.5" -> 0.5" gap
    // (inside standoff); at 2.5" -> 1.5" gap (chargeable, not forced); at 10" -> far.
    [TestFixture]
    public class ChooseActionPassDisableTests
    {
        [Test]
        public void GetCanPass_NoEnemyAtAll_True()
        {
            var (ctx, unitCtx) = Build();

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void GetCanPass_EnemyFarAway_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(10, 0));

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out _);

            Assert.That(canPass, Is.True);
        }

        // The #206 core: moving a long way no longer gates Pass. Previously a beyond-Rush move locked Pass off;
        // now only live proximity does. With no enemy nearby, a 20" move still leaves Pass available.
        [Test]
        public void GetCanPass_MovedFar_ButNoEnemyNearby_True()
        {
            var (ctx, unitCtx) = Build();
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 8f, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"distance moved must not gate Pass any more (#206). Reason given: {reason}");
        }

        [Test]
        public void GetCanPass_EnemyWithinStandoff_False()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0)); // 0.5" base-to-base

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.False);
            Assert.That(reason, Is.Not.Null.And.Contains("charge"));
        }

        // The forced band (1") is narrower than the charge band (2"): a unit at 1.5" gap may Charge, but is not
        // forced to - it may still Pass. This is the state a Teleport lands you in when it takes you just clear
        // of the standoff but not out of charge range.
        [Test]
        public void GetCanPass_EnemyChargeableButBeyondStandoff_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(2.5f, 0)); // 1.5" base-to-base

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"an enemy in the 1\"-2\" band is chargeable but not forced. Reason given: {reason}");
        }

        // Once the unit has attacked it has followed through on the engage obligation; the flow loops back here
        // after the melee and the proximity gate must not re-fire (it is standing in contact, by definition).
        [Test]
        public void GetCanPass_EnemyWithinStandoff_ButAlreadyAttacked_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0));
            unitCtx.RegisterAttackedFinished();

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"a unit that already engaged must be allowed to Pass. Reason given: {reason}");
        }

        // Only ENEMIES force a charge. An allied unit sitting inside the standoff (shared team) must not gate Pass.
        [Test]
        public void GetCanPass_AlliedUnitWithinStandoff_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0), otherIsAlly: true);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"an allied model in the standoff must not force a charge. Reason given: {reason}");
        }

        private static (TestGameContext ctx, UnitActionContext unitCtx) Build(
            Position? enemyAt = null, bool otherIsAlly = false,
            Action<DataBinding<UnitData>>? configureUnit = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));

            var playerID = new PlayerID(Guid.NewGuid());
            var model = MakeModel(store, new Position(0, 0));
            var unit = MakeUnit(store, playerID, new[] { model });
            configureUnit?.Invoke(unit);
            store.Create(new ArmyData(playerID, new List<DataBinding<UnitData>> { unit }));

            if (enemyAt.HasValue)
            {
                var otherPlayer = new PlayerID(Guid.NewGuid());
                var otherModel = MakeModel(store, enemyAt.Value);
                var otherUnit = MakeUnit(store, otherPlayer, new[] { otherModel });
                store.Create(new ArmyData(otherPlayer, new List<DataBinding<UnitData>> { otherUnit }));

                // A shared team makes the other army an ALLY (excluded from the forced-charge check); with no
                // TeamData the activating player is its own team, so a different-player army is an enemy.
                if (otherIsAlly)
                    store.Create(new TeamData(0, new List<PlayerID> { playerID, otherPlayer }));
            }

            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return (ctx, unitCtx);
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, "Test Unit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            var unitRef = store.Create(unit);
            return store.GetDataBinding<UnitData>(unitRef);
        }
    }
}
