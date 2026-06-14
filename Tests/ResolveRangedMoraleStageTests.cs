using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #009 — A unit reduced to half strength or less by shooting takes a morale test; because the
    // trigger is being at half strength, a failed test Routs it. The test fires only on the weapon that
    // *crosses* the unit into half strength (DefenderRemainingWoundsAtStart is snapshotted before each
    // fire), so a later weapon at an already-sub-half target doesn't re-test. Quality is 4 throughout, so
    // FixedDiceRoller(>=4) passes and FixedDiceRoller(<4) fails.
    [TestFixture]
    public class ResolveRangedMoraleStageTests
    {
        [Test]
        public async Task ReducedToHalfStrength_FailsTest_IsRouted()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1);
            combat.SetDefender(defender);          // snapshot at full strength (above half)
            KillModels(defender, 2);               // this fire drops it to 2 of 4 — half strength

            bool finished = await RunResolve(combat);

            Assert.That(finished, Is.True);
            Assert.That(defender.GetValue().GetIsDead(), Is.True, "a failed half-strength morale test Routs the unit.");
        }

        [Test]
        public async Task ReducedToHalfStrength_PassesTest_Survives()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 6);
            combat.SetDefender(defender);
            KillModels(defender, 2);

            await RunResolve(combat);

            Assert.That(defender.GetValue().GetIsAlive(), Is.True, "a passed test leaves the unit on the table.");
            Assert.That(defender.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "shooting morale never applies Shaken — it Routs on a fail or does nothing.");
        }

        [Test]
        public async Task StaysAboveHalfStrength_NoTest()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1); // would fail a test if one were taken
            combat.SetDefender(defender);
            KillModels(defender, 1);               // 3 of 4 remain — above half

            await RunResolve(combat);

            Assert.That(defender.GetValue().GetIsAlive(), Is.True, "no test is taken above half strength, so the failing die is irrelevant.");
        }

        [Test]
        public async Task AlreadyAtHalfBeforeThisFire_NoReTest()
        {
            var (combat, defender) = BuildShoot(defenderModels: 4, dieValue: 1);
            KillModels(defender, 2);               // already at half BEFORE this weapon
            combat.SetDefender(defender);          // snapshot taken at half strength

            await RunResolve(combat);

            Assert.That(defender.GetValue().GetIsAlive(), Is.True,
                "a unit already at half strength before the fire isn't re-tested — guards against double jeopardy from multi-weapon fire.");
        }

        [Test]
        public async Task DestroyedOutright_NoTestNoError()
        {
            var (combat, defender) = BuildShoot(defenderModels: 2, dieValue: 6);
            combat.SetDefender(defender);
            KillModels(defender, 2);               // wiped out by the fire

            bool finished = await RunResolve(combat);

            Assert.That(finished, Is.True, "a unit destroyed outright just passes through to the next stage.");
            Assert.That(defender.GetValue().GetIsDead(), Is.True);
        }

        // Helpers

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
