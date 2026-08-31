using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
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

        // #337 — the gate measures BASE to BASE, not centre to centre, and the difference is the whole
        // rule once bases get big. Two 3"-diameter circular bases whose centres are 3.5" apart are 0.5"
        // of table apart: plainly inside the 1" standoff, and a centre-distance implementation would call
        // them four times clear of it. Circles on both sides deliberately — that is the pairing a playtest
        // reported as "got really really close and didn't have to charge" (2026-08-04), and the pairing
        // where a bug would be least visible, since a circle needs no facing to measure.
        [Test]
        public void GetCanPass_LargeCircularBases_MeasuredBaseToBase_NotCentreToCentre()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(3.5f, 0), baseRadius: 1.5f);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.False,
                "centres 3.5\" apart but bases only 0.5\" apart - a centre-distance check would wrongly "
                + $"allow Pass here. Reason given: {reason}");
        }

        // The other side of the same coin: big bases must not force a charge on a unit that is genuinely
        // clear. Centres 5.1" apart, bases 2.1" apart - outside the standoff AND outside melee range.
        [Test]
        public void GetCanPass_LargeCircularBases_GenuinelyClear_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(5.1f, 0), baseRadius: 1.5f);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"bases 2.1\" apart are clear of the 1\" standoff. Reason given: {reason}");
        }

        // #337 — the case the playtest actually hit. A unit that STARTED its activation Shaken never
        // reaches the action menu at all (ChooseActionStage short-circuits to end-of-activation), so the
        // forced-charge gate is bypassed no matter how close it is standing. Pinned because it is easy to
        // read as the proximity rule failing, and because it is exactly why the picker now badges Shaken.
        [Test]
        public void StartedActivationShaken_BypassesTheForcedChargeGate_ByDesign()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0), shaken: true);

            Assert.That(unitCtx.StartedActivationShaken, Is.True);
            Assert.That(ChooseActionStage.GetCanPass(ctx, unitCtx, out _), Is.False,
                "the gate itself still says no - it is simply never consulted for a Shaken activation.");
        }

        // #390 — the shoot half of the same obligation. A unit inside the standoff band that CAN charge must
        // not be offered a shot: shooting sets HasAttacked, which closes the charge gate and satisfies
        // GetCanPass's engaged short-circuit, so the shot would both dodge and dissolve the forced charge.
        [Test]
        public void ShootForfeit_EnemyWithinStandoff_ChargeAvailable_True()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0)); // 0.5" base-to-base

            bool forfeits = ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: true);

            Assert.That(forfeits, Is.True,
                "inside the standoff band with a charge available, Shoot must be withheld - it would forfeit the charge.");
        }

        // The 1"-2" band is chargeable but NOT forced (same boundary as the Pass gate): the unit may shoot.
        [Test]
        public void ShootForfeit_EnemyChargeableButBeyondStandoff_False()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(2.5f, 0)); // 1.5" base-to-base

            bool forfeits = ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: true);

            Assert.That(forfeits, Is.False,
                "an enemy in the 1\"-2\" band is chargeable but not forced - the shot stays available.");
        }

        // A unit that cannot charge at all (Immobile, only Aircraft in range, nothing to swing) owes nothing:
        // withholding its shot would punish it for an obligation it cannot discharge.
        [Test]
        public void ShootForfeit_EnemyWithinStandoff_ButNoChargeAvailable_False()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0));

            bool forfeits = ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: false);

            Assert.That(forfeits, Is.False,
                "with no charge on offer the obligation cannot bind - the unit keeps its shot.");
        }

        // Allies never force a charge (same team screening as the Pass gate).
        [Test]
        public void ShootForfeit_AlliedUnitWithinStandoff_False()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(1.5f, 0), otherIsAlly: true);

            bool forfeits = ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: true);

            Assert.That(forfeits, Is.False,
                "an allied model in the standoff must not withhold the shot.");
        }

        // Base-to-base measurement, same geometry pin as the Pass gate (#337): centres 3.5" apart but
        // 3"-diameter bases only 0.5" apart - inside the band, shot withheld.
        [Test]
        public void ShootForfeit_LargeCircularBases_MeasuredBaseToBase()
        {
            var (ctx, unitCtx) = Build(enemyAt: new Position(3.5f, 0), baseRadius: 1.5f);

            bool forfeits = ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: true);

            Assert.That(forfeits, Is.True,
                "bases 0.5\" apart are inside the standoff regardless of centre distance - the shot is withheld.");
        }

        private static (TestGameContext ctx, UnitActionContext unitCtx) Build(
            Position? enemyAt = null, bool otherIsAlly = false, float baseRadius = 0.5f,
            bool shaken = false,
            Action<DataBinding<UnitData>>? configureUnit = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));

            var playerID = new PlayerID(Guid.NewGuid());
            var model = MakeModel(store, new Position(0, 0), baseRadius);
            var unit = MakeUnit(store, playerID, new[] { model });
            if (shaken) unit.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(Rules.Foundation.TokenType.Shaken));
            configureUnit?.Invoke(unit);
            store.Create(new ArmyData(playerID, new List<DataBinding<UnitData>> { unit }));

            if (enemyAt.HasValue)
            {
                var otherPlayer = new PlayerID(Guid.NewGuid());
                var otherModel = MakeModel(store, enemyAt.Value, baseRadius);
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

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position,
            float baseRadiusInches = 0.5f)
        {
            var model = new ModelData(
                baseRadiusInches: baseRadiusInches,
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
