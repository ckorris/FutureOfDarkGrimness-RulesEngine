using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #089 — A failed melee morale test makes the losing unit Shaken, or Routs it (removes it from
    // play by killing every living model) if it is already at half strength or less. The stage reads
    // the losing unit from the DetermineMoraleSaveNeededResult that the preceding stage records, so
    // these tests seed that result directly, set the unit's state, then run the outcome stage.
    [TestFixture]
    public class AssignMeleeMoralePenaltyStageTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public async Task MultiModelAboveHalfStrength_BecomesShaken()
        {
            var (combat, _, defender) = BuildMelee(defenderModels: 4, woundsPerModel: 1);

            await RunPenalty(combat, loser: defender);

            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
            Assert.That(defender.GetValue().GetIsAlive(), Is.True);
        }

        [Test]
        public async Task MultiModelAtHalfStrength_IsRouted()
        {
            var (combat, _, defender) = BuildMelee(defenderModels: 2, woundsPerModel: 1);
            // Kill one of two models, leaving the unit at exactly half strength.
            KillModel(defender, 0);

            await RunPenalty(combat, loser: defender);

            Assert.That(defender.GetValue().GetIsDead(), Is.True);
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task SingleModelAboveHalfWounds_BecomesShaken()
        {
            var (combat, _, defender) = BuildMelee(defenderModels: 1, woundsPerModel: 6);
            // 4 of 6 wounds remain — above half, so Shaken rather than Routed.
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);

            await RunPenalty(combat, loser: defender);

            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
            Assert.That(defender.GetValue().GetIsAlive(), Is.True);
        }

        [Test]
        public async Task SingleModelAtHalfWounds_IsRouted()
        {
            var (combat, _, defender) = BuildMelee(defenderModels: 1, woundsPerModel: 6);
            // 3 of 6 wounds remain — exactly half, so the failed test Routs it.
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(3);

            await RunPenalty(combat, loser: defender);

            Assert.That(defender.GetValue().GetIsDead(), Is.True);
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        // Helpers

        private async Task RunPenalty(CombatActionContext combat, DataBinding<UnitData> loser)
        {
            combat.AddResult(new DetermineMoraleSaveNeededResult(loser.GetValue().Quality, loser));

            var stage = new AssignMeleeMoralePenaltyStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnAssignedPenalty.Bind("AssignedPenalty");
            await stage.Enter(combat);
        }

        private static void KillModel(DataBinding<UnitData> unit, int modelIndex)
        {
            var model = unit.GetValue().ModelBindings[modelIndex].GetValue();
            model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        private (CombatActionContext Combat, DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender)
            BuildMelee(int defenderModels, int woundsPerModel)
        {
            var attacker = MakeUnit("Attacker", modelCount: 1, woundsPerModel: 5, new Position(0, 0));
            var defender = MakeUnit("Defender", defenderModels, woundsPerModel, new Position(2, 0));

            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            return (combat, attacker, defender);
        }

        private DataBinding<UnitData> MakeUnit(string name, int modelCount, int woundsPerModel, Position position)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    initialPosition: position,
                    gameDataStore: _store);
                model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
