using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // The attack visual must fire from the models that actually carry the weapon, not the whole
    // unit — otherwise a single rifle among five models would appear to come from the wrong place.
    [TestFixture]
    public class AttackBeatPositionsTests
    {
        private static GameDataStore NewStore() =>
            new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(16)
                .RegisterType<Position>(16)
                .RegisterType<ModelData>(16)
                .RegisterType<UnitData>(4)
                .Build();

        private static DataBinding<ModelData> MakeModel(GameDataStore store, List<Weapon> weapons, Position pos)
        {
            var model = new ModelData(0.5f, weapons, new List<SpecialRule>(), pos, store);
            DataReference reference = store.Create(model);
            return store.GetDataBinding<ModelData>(reference);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, List<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "U", 4, 4, new List<SpecialRule>(), models);
            DataReference reference = store.Create(unit);
            return store.GetDataBinding<UnitData>(reference);
        }

        private static Weapon W(string name) => new Weapon(name, 24f, 1, 0, new HashSet<ISpecialRule_Weapon>());

        [Test]
        public void FiringModels_ReturnsOnlyTheWeaponCarryingModels()
        {
            GameDataStore store = NewStore();
            Weapon rifle = W("Rifle");
            Weapon pistol = W("Pistol");

            // Five models; only one carries the rifle.
            var rifleModel = MakeModel(store, new List<Weapon> { rifle }, new Position(5f, 5f));
            var others = new List<DataBinding<ModelData>>
            {
                rifleModel,
                MakeModel(store, new List<Weapon> { pistol }, new Position(6f, 6f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(7f, 7f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(8f, 8f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(9f, 9f)),
            };
            var unit = MakeUnit(store, others);

            List<Position> firingRifle = AttackBeatPositions.FiringModels(unit, rifle);
            Assert.That(firingRifle, Has.Count.EqualTo(1), "only the rifle-carrying model fires the rifle");
            Assert.That(firingRifle[0].x, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(firingRifle[0].z, Is.EqualTo(5f).Within(0.0001f));

            List<Position> firingPistol = AttackBeatPositions.FiringModels(unit, pistol);
            Assert.That(firingPistol, Has.Count.EqualTo(4), "the four pistol models fire the pistol");
        }

        [Test]
        public void FiringModels_ExcludesUnplacedAndDeadModels()
        {
            GameDataStore store = NewStore();
            Weapon rifle = W("Rifle");

            var placed = MakeModel(store, new List<Weapon> { rifle }, new Position(5f, 5f));
            var unplaced = MakeModel(store, new List<Weapon> { rifle }, new Position()); // (0,0,0) reserve
            var dead = MakeModel(store, new List<Weapon> { rifle }, new Position(3f, 3f));
            dead.GetValue().DealWounds(dead.GetValue().TotalWounds);

            var unit = MakeUnit(store, new List<DataBinding<ModelData>> { placed, unplaced, dead });

            List<Position> firing = AttackBeatPositions.FiringModels(unit, rifle);
            Assert.That(firing, Has.Count.EqualTo(1), "unplaced (reserve) and dead carriers don't fire");
            Assert.That(firing[0].x, Is.EqualTo(5f).Within(0.0001f));
        }
    }
}
