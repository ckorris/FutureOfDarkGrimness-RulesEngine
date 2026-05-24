using System;
using System.Collections.Generic;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class UnitIDTests
    {
        [Test]
        public void ID_AssignedOnConstruction_IsNotDefault()
        {
            var unit = MakeBareUnit();

            Assert.That(unit.ID, Is.Not.EqualTo(default(UnitID)));
            Assert.That(unit.ID.ID, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void ID_TwoUnits_HaveDifferentIDs()
        {
            var a = MakeBareUnit();
            var b = MakeBareUnit();

            Assert.That(a.ID, Is.Not.EqualTo(b.ID));
        }

        [Test]
        public void ID_FromJsonConstructorWithExplicitID_UsesProvidedValue()
        {
            var providedId = new UnitID(Guid.NewGuid());

            var unit = new UnitData(
                playerID: new PlayerID(Guid.NewGuid()),
                name: "Explicit-ID Unit",
                quality: 4,
                defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>>(),
                id: providedId);

            Assert.That(unit.ID, Is.EqualTo(providedId));
        }

        [Test]
        public void ID_SurvivesGameDataStoreRoundTrip()
        {
            // Mirrors the UnitData round-trip pattern in MessageSerializationTests:
            // serialise from one store, rehydrate in a fresh one, and assert the ID matches.
            // This covers both save/load (same machine, different time) and network transmission
            // (different machines, same time) since they both flow through GameDataStore JSON.

            GameDataStore fromStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .Build();

            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: new Position(),
                fromStore);
            DataReference modelRef = fromStore.Create(modelData);
            DataBinding<ModelData> modelBinding = fromStore.GetDataBinding<ModelData>(modelRef);

            var originalUnit = new UnitData(
                playerID: new PlayerID(Guid.NewGuid()),
                name: "Round-Trip Unit",
                quality: 5,
                defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataReference unitRef = fromStore.Create(originalUnit);

            UnitID originalID = originalUnit.ID;

            // Serialise the chain (wounds + position + model + unit) so the rehydrated unit
            // can find its model binding on the other side.
            string serializedWounds   = fromStore.GetValueAsJson<float>(modelData.RemainingWoundsBinding.Reference);
            string serializedPosition = fromStore.GetValueAsJson<Position>(modelData.PositionBinding.Reference);
            string serializedModel    = fromStore.GetValueAsJson<ModelData>(modelRef);
            string serializedUnit     = fromStore.GetValueAsJson<UnitData>(unitRef);

            GameDataStore toStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .Build();

            toStore.CreateFromReferenceAndJson(modelData.RemainingWoundsBinding.Reference, serializedWounds);
            toStore.CreateFromReferenceAndJson(modelData.PositionBinding.Reference, serializedPosition);
            toStore.CreateFromReferenceAndJson(modelRef, serializedModel);
            toStore.CreateFromReferenceAndJson(unitRef, serializedUnit);

            UnitData deserializedUnit = toStore.GetValue<UnitData>(unitRef);

            Assert.That(deserializedUnit.ID, Is.EqualTo(originalID),
                "UnitID must survive serialisation round-trip — broken IDs would silently invalidate save/load and break cross-unit token ownership across the network.");
        }

        /// <summary>
        /// Constructs a minimal <see cref="UnitData"/> with no models. Sufficient for tests
        /// that only need the unit's identity and don't exercise the rest of the engine.
        /// </summary>
        private static UnitData MakeBareUnit() =>
            new UnitData(
                playerID: new PlayerID(Guid.NewGuid()),
                name: "TestUnit",
                quality: 4,
                defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>>());
    }
}
