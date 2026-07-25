using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #009 — A unit reduced to half strength or less by shooting takes a morale test; a failed test makes
    // it Shaken (a non-melee morale failure never Routs — Rout is a melee-only result, GF v3.5.1).
    //
    // The stage runs ONCE, after the attacker has fired every weapon, and tests each unit it shot at
    // exactly once. "Took wounds" is measured against the defender's wounds at the moment it was first
    // targeted (captured by RegisterAttackedDefender), so a unit shot by three weapons tests at most
    // once. A unit already at half or less tests again every activation it takes wounds (#254) — the
    // rule keys on wounds leaving the unit at half or less, not on crossing the boundary.
    //
    // Quality is 4 throughout, so FixedDiceRoller(>=4) passes and FixedDiceRoller(<4) fails.
    [TestFixture]
    public class ResolveRangedMoraleStageTests
    {
        [Test]
        public async Task ReducedToHalfStrength_FailsTest_IsShaken()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1);
            combat.RegisterAttackedDefender(defender);  // snapshot at full strength (above half)
            KillModels(defender, 2);                    // the shooting drops it to 2 of 4 — half strength

            bool finished = await RunResolve(combat);

            Assert.That(finished, Is.True);
            Assert.That(defender.GetValue().GetIsAlive(), Is.True,
                "a failed non-melee morale test makes the unit Shaken, not destroyed — shooting never Routs.");
            Assert.That(IsShaken(defender), Is.True,
                "the surviving-but-failed unit carries the Shaken token.");
        }

        [Test]
        public async Task ReducedToHalfStrength_PassesTest_Survives()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 6);
            combat.RegisterAttackedDefender(defender);
            KillModels(defender, 2);

            await RunResolve(combat);

            Assert.That(defender.GetValue().GetIsAlive(), Is.True, "a passed test leaves the unit on the table.");
            Assert.That(IsShaken(defender), Is.False,
                "a passed morale test applies nothing — only a failed test makes the unit Shaken.");
        }

        [Test]
        public async Task StaysAboveHalfStrength_NoTest()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1); // would fail a test if one were taken
            combat.RegisterAttackedDefender(defender);
            KillModels(defender, 1);                    // 3 of 4 remain — above half

            await RunResolve(combat);

            Assert.That(IsShaken(defender), Is.False, "no test is taken above half strength, so the failing die is irrelevant.");
        }

        // #254: the rule is "wounds leave the unit at half or less", not "the unit crossed into half" —
        // a unit already below half tests again every activation it takes wounds.
        [Test]
        public async Task AlreadyAtHalfBeforeShooting_WoundedAgain_TestsAgain()
        {
            var (combat, defender) = BuildShoot(defenderModels: 6, dieValue: 1);
            KillModels(defender, 3);                    // already at half BEFORE this attacker targeted it
            combat.RegisterAttackedDefender(defender);  // snapshot taken at half strength
            KillModels(defender, 1);                    // the shooting wounds it again while at half

            await RunResolve(combat);

            Assert.That(IsShaken(defender), Is.True,
                "a unit at half strength that takes wounds from shooting tests again (and this die fails).");
        }

        [Test]
        public async Task AlreadyAtHalf_TargetedButUnwounded_NoTest()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1);
            KillModels(defender, 2);                    // at half before the shooting
            combat.RegisterAttackedDefender(defender);  // targeted, but every shot misses or is saved

            await RunResolve(combat);

            Assert.That(IsShaken(defender), Is.False,
                "morale keys on taking wounds — a targeted unit that lost none doesn't test, even at half.");
        }

        // #278: the playtest-reported case, driven through the REAL save chain. A unit already at half
        // gets HIT, but every save passes — zero wounds reach the unit, so no morale test fires.
        // AlreadyAtHalf_TargetedButUnwounded_NoTest pins the same predicate but fakes "all saved" by
        // dealing no wounds; this one runs RollToSave -> AssignWounds -> ApplyWounds for real and proves
        // the saved volley leaves RemainingWounds at the baseline. The die face is 3: it passes the 2+
        // saves but would FAIL the Quality-4 morale test, so a wrongly-taken test would show as Shaken.
        [Test]
        public async Task AlreadyAtHalf_HitButEverySavePasses_NoTest()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedFaceDiceRoller(3));
            var attacker = MakeUnit(store, "Attacker", modelCount: 3, new Position(0, 0));
            var defender = MakeUnit(store, "Defender", modelCount: 4, new Position(10, 0));
            KillModels(defender, 2);                    // at half BEFORE the volley
            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            combat.RegisterAttackedDefender(defender);  // targeted: snapshot taken at half
            float baseline = defender.GetValue().RemainingWounds;

            // The per-weapon tail: 3 hits landed, saving on 2+ (every rolled 3 passes).
            var weapon = new Weapon("Test Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 3);
            metadata.AddResult(new DetermineSaveRollNeededResults(new List<PendingSaveRolls>
                { new PendingSaveRolls(RulesHarness.TestDice.Faces(3, 3, 3), saveNeeded: 2) }));
            await RunCombatStage(new RollToSaveStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunCombatStage(new AssignWoundsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunCombatStage(new ApplyWoundsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);

            Assert.That(defender.GetValue().RemainingWounds, Is.EqualTo(baseline).Within(0.001f),
                "every hit was saved - no wounds reached the unit.");

            await RunResolve(combat);

            Assert.That(IsShaken(defender), Is.False,
                "an all-saved volley deals no wounds, so a unit at half takes no morale test.");
        }

        [Test]
        public async Task DestroyedOutright_NoTestNoError()
        {
            var (combat, defender) = BuildShoot(defenderModels: 2, dieValue: 6);
            combat.RegisterAttackedDefender(defender);
            KillModels(defender, 2);                    // wiped out by the fire

            bool finished = await RunResolve(combat);

            Assert.That(finished, Is.True, "a unit destroyed outright just passes through to the next stage.");
            Assert.That(defender.GetValue().GetIsDead(), Is.True);
        }

        [Test]
        public async Task NothingWasShotAt_NoTestNoError()
        {
            var (combat, _) = BuildShoot(defenderModels: 4, dieValue: 1);

            bool finished = await RunResolve(combat);

            Assert.That(finished, Is.True, "a shoot action that never picked a target passes straight through.");
        }

        // The bug this stage was moved to fix: morale used to run inside the per-weapon loop and only ever
        // tested the CURRENT weapon's target, so a unit shot by a second weapon never rolled.
        [Test]
        public async Task EveryDefenderShotAt_IsTestedOnce()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(1));
            var attacker = MakeUnit(store, "Attacker", modelCount: 1, new Position(0, 0));
            DataBinding<UnitData> first  = MakeUnit(store, "Defender1", modelCount: 4, new Position(10, 0));
            DataBinding<UnitData> second = MakeUnit(store, "Defender2", modelCount: 4, new Position(20, 0));
            var combat = new CombatActionContext(ctx, attacker, isMelee: false);

            combat.RegisterAttackedDefender(first);
            combat.RegisterAttackedDefender(second);
            KillModels(first, 2);
            KillModels(second, 2);

            await RunResolve(combat);

            Assert.That(IsShaken(first), Is.True, "the first weapon's target is tested.");
            Assert.That(IsShaken(second), Is.True,
                "the second weapon's target is tested too — morale is resolved once per unit that was shot at.");
        }

        // The other half of the fix: the wounds baseline is captured on FIRST sight, so a defender targeted
        // by several weapons is measured against the wounds it had before any of them fired.
        [Test]
        public async Task DefenderTargetedBySeveralWeapons_SnapshotTakenOnFirstSight()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1);

            combat.RegisterAttackedDefender(defender);  // weapon 1 declares: 4 of 4
            KillModels(defender, 1);                    // weapon 1 kills one -> 3 of 4, still above half
            combat.RegisterAttackedDefender(defender);  // weapon 2 declares at the same unit — must NOT re-baseline
            KillModels(defender, 1);                    // weapon 2 kills one -> 2 of 4, crossed into half

            await RunResolve(combat);

            Assert.That(combat.AttackedDefenders.Count, Is.EqualTo(1), "the same defender is recorded once.");
            Assert.That(combat.AttackedDefenders[0].RemainingWoundsAtStart, Is.EqualTo(4f).Within(0.001f),
                "the baseline is the wound count before the first weapon fired, not before the last one.");
            Assert.That(IsShaken(defender), Is.True,
                "the action's wounds left the unit at half strength, so it tests once at the end.");
        }

        // Helpers

        private static bool IsShaken(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken);

        private static async Task RunCombatStage<TResult, TStage>(
            CombatStage<TResult, TStage, ICombatMetadata> stage, CombatMetadata metadata)
            where TStage : CombatStage<TResult, TStage, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        private static async Task<bool> RunResolve(CombatActionContext combat)
        {
            var ctx = (TestGameContext)combat.GameContext;
            var stage = new ResolveRangedMoraleStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool finished = false;
            stage.ToFinished.Bind("ToFinished");
            stage.ToFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(combat);
            return finished;
        }

        private static void KillModels(DataBinding<UnitData> unit, int count)
        {
            int killed = 0;
            foreach (var modelBinding in unit.GetValue().ModelBindings)
            {
                if (killed >= count) break;
                var model = modelBinding.GetValue();
                if (!model.GetIsAlive()) continue;
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
                killed++;
            }
        }

        private static (CombatActionContext Combat, DataBinding<UnitData> Defender) BuildShoot(int defenderModels, int dieValue)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(dieValue));

            var attacker = MakeUnit(store, "Attacker", modelCount: 1, new Position(0, 0));
            var defender = MakeUnit(store, "Defender", defenderModels, new Position(10, 0));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            return (combat, defender);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name, int modelCount, Position position)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    initialPosition: position,
                    gameDataStore: store);
                modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
