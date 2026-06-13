using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class IUnitWeaponsTests
    {
        [Test]
        public void GetRangedWeapons_ExcludesDeadModelsWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());

            var liveModel = MakeModel(store, Rifle());
            var doomedModel = MakeModel(store, Rifle());
            var unit = MakeUnit(store, playerID, new[] { liveModel, doomedModel });

            Assume.That(unit.GetValue().GetRangedWeapons().Count, Is.EqualTo(2));

            doomedModel.GetValue().DealWounds(doomedModel.GetValue().TotalWounds);

            Assert.That(doomedModel.GetValue().GetIsAlive(), Is.False);
            Assert.That(unit.GetValue().GetRangedWeapons().Count, Is.EqualTo(1));
        }

        [Test]
        public void GetMeleeWeapons_ExcludesDeadModelsWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());

            var liveModel = MakeModel(store, Sword());
            var doomedModel = MakeModel(store, Sword());
            var unit = MakeUnit(store, playerID, new[] { liveModel, doomedModel });

            Assume.That(unit.GetValue().GetMeleeWeapons().Count, Is.EqualTo(2));

            doomedModel.GetValue().DealWounds(doomedModel.GetValue().TotalWounds);

            Assert.That(unit.GetValue().GetMeleeWeapons().Count, Is.EqualTo(1));
        }

        [Test]
        public void AllWeapons_NoPredicate_ExcludesDeadModelsWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());

            var liveModel = MakeModel(store, Rifle());
            var doomedModel = MakeModel(store, Rifle(), Sword());
            var unit = MakeUnit(store, playerID, new[] { liveModel, doomedModel });

            Assume.That(unit.GetValue().AllWeapons().Count, Is.EqualTo(3));

            doomedModel.GetValue().DealWounds(doomedModel.GetValue().TotalWounds);

            Assert.That(unit.GetValue().AllWeapons().Count, Is.EqualTo(1));
        }

        private static Weapon Rifle() =>
            new Weapon("Rifle", 24f, 1, 0);

        private static Weapon Sword() =>
            new Weapon("Sword", 0f, 1, 0);

        private static DataBinding<ModelData> MakeModel(GameDataStore store, params Weapon[] weapons)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: weapons.ToList(),
                initialPosition: new Position(0, 0, 0),
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            var unitRef = store.Create(unit);
            return store.GetDataBinding<UnitData>(unitRef);
        }
    }
}
