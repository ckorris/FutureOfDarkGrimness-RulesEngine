using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #263 — an off-table unit (in reserve, embarked, or flown off the edge) must be unreachable by
    // the melee/charge family. A reserve unit's models still carry their default origin position, so
    // before the fix raw geometry read an Ambush unit as standing in the table's bottom-left corner:
    // enemies deployed near the corner could charge it in round 1 — and inside the 1" standoff band
    // were FORCED to (#206 gated Pass off). The fix lives at the shared chokepoint
    // (MeleeRangeUtilities.AreUnitsInMeleeRange, which the charge-availability gate and defender
    // eligibility both flow through) plus an explicit filter in the standoff scan, so every test
    // pairs the reserve case with an on-table control proving the geometry alone WOULD engage.
    [TestFixture]
    public class OffTableTargetingTests
    {
        // Attacker model at (1.5, 0), radius 0.5" — base-to-base gap to a model at the origin is
        // 0.5": inside the 1" forced-charge standoff AND the 2" melee band. Reserve tests leave the
        // enemy model at the origin (the default position every never-placed model has); the on-table
        // controls place it at (0.5, 0) instead, because a model centred exactly at (0,0) IS the
        // engine's never-placed marker and cannot occur in real play — the controls prove the same
        // corner-adjacent geometry engages when the unit is genuinely on the table.
        private static readonly Position AttackerNearOrigin = new Position(1.5f, 0f);
        private static readonly Position AtOrigin = new Position(0f, 0f);
        private static readonly Position NearOrigin = new Position(0.5f, 0f);

        [Test]
        public void MeleeRange_OnTableEnemyNearOrigin_InRange_Control()
        {
            var (_, _, attacker, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: NearOrigin);

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(attacker.GetValue(), enemy.GetValue()),
                Is.True, "control: the geometry alone puts these units in melee range");
        }

        [Test]
        public void MeleeRange_ReserveEnemyAtOrigin_NotInRange()
        {
            var (_, _, attacker, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: AtOrigin);
            ReserveRules.PlaceInReserve(enemy.GetValue());

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(attacker.GetValue(), enemy.GetValue()),
                Is.False, "a unit held in reserve is in melee range of nothing (#263)");
        }

        [Test]
        public void MeleeRange_ReserveAttacker_NotInRange()
        {
            var (_, _, attacker, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: NearOrigin);
            ReserveRules.PlaceInReserve(attacker.GetValue());

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(attacker.GetValue(), enemy.GetValue()),
                Is.False, "the gate works in both directions: a reserve unit can't engage either");
        }

        [Test]
        public void MeleeRange_EmbarkedEnemy_NotInRange()
        {
            var (_, _, attacker, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: NearOrigin);
            TransportUtilities.Embark(enemy.GetValue(), attacker.GetValue());

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(attacker.GetValue(), enemy.GetValue()),
                Is.False, "an embarked unit is off-table and can't be engaged");
        }

        [Test]
        public void GetCanPass_OnTableEnemyNearOrigin_ForcedToCharge_Control()
        {
            var (ctx, unitCtx, _, _) = Build(attackerAt: AttackerNearOrigin, enemyAt: NearOrigin);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out _);

            Assert.That(canPass, Is.False,
                "control: an ON-table enemy inside the standoff band forces the charge (#206)");
        }

        [Test]
        public void GetCanPass_ReserveEnemyAtOrigin_NotForced()
        {
            var (ctx, unitCtx, _, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: AtOrigin);
            ReserveRules.PlaceInReserve(enemy.GetValue());

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"a reserve unit 'at' the origin must not force a charge (#263). Reason given: {reason}");
        }

        // The #263 backstop: wounds landing on a unit that carries an off-table token mean an
        // upstream targeting filter is missing — RuleDiagnostics must announce it.
        [Test]
        public void Wounds_OnReserveUnit_RaisesDiagnosticWarning()
        {
            var (_, _, _, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: AtOrigin);
            ReserveRules.PlaceInReserve(enemy.GetValue());

            string? warning = CaptureWarning(() =>
                ((IModel)enemy.GetValue().Models[0]).DealWounds(1f));

            Assert.That(warning, Is.Not.Null, "wounding an off-table unit must warn");
            Assert.That(warning, Does.Contain("off the battlefield"));
        }

        [Test]
        public void Wounds_OnTableUnit_NoWarning_EvenWhenKilled()
        {
            var (_, _, _, enemy) = Build(attackerAt: AttackerNearOrigin, enemyAt: NearOrigin);

            // Overkill the single model: the unit now reads as off-battlefield by POSITION (no
            // living model anywhere), which is exactly why the backstop checks tokens instead.
            string? warning = CaptureWarning(() =>
                ((IModel)enemy.GetValue().Models[0]).DealWounds(10f));

            Assert.That(warning, Is.Null,
                "an ordinary on-table kill must not trip the off-table backstop");
        }

        private static string? CaptureWarning(Action act)
        {
            string? captured = null;
            void Capture(string message) => captured = message;

            RuleDiagnostics.OnWarning += Capture;
            try
            {
                act();
            }
            finally
            {
                RuleDiagnostics.OnWarning -= Capture;
            }
            return captured;
        }

        // Two opposing single-model armies at the given positions.
        private static (TestGameContext ctx, UnitActionContext unitCtx,
            DataBinding<UnitData> attacker, DataBinding<UnitData> enemy) Build(
            Position attackerAt, Position enemyAt)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));

            var playerID = new PlayerID(Guid.NewGuid());
            var attacker = MakeUnit(store, playerID, attackerAt);
            store.Create(new ArmyData(playerID, new List<DataBinding<UnitData>> { attacker }));

            var enemyPlayer = new PlayerID(Guid.NewGuid());
            var enemy = MakeUnit(store, enemyPlayer, enemyAt);
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemy }));

            var unitCtx = new UnitActionContext(ctx, attacker);
            unitCtx.Reset(attacker);
            return (ctx, unitCtx, attacker, enemy);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID,
            Position modelAt)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: modelAt,
                gameDataStore: store);
            var modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

            var unit = new UnitData(playerID, "Test Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            // The simple ctor doesn't subscribe wound aggregation; the backstop tests need it live.
            unit.RewireModelWoundSubscriptions();
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
