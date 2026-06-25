using System;
using System.Collections.Generic;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class ModelIDTests
    {
        [Test]
        public void ID_AssignedOnConstruction_IsNotDefault()
        {
            var model = MakeBareModel(NewStore());

            Assert.That(model.ID, Is.Not.EqualTo(default(ModelID)));
            Assert.That(model.ID.ID, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void ID_TwoModels_HaveDifferentIDs()
        {
            GameDataStore store = NewStore();
            var a = MakeBareModel(store);
            var b = MakeBareModel(store);

            Assert.That(a.ID, Is.Not.EqualTo(b.ID));
        }

        [Test]
        public void ID_FromJsonConstructorWithExplicitID_UsesProvidedValue()
        {
            GameDataStore store = NewStore();
            var providedId = new ModelID(Guid.NewGuid());

            DataReference woundsRef = store.Create(1f);
            DataReference positionRef = store.Create(new Position());

            var model = new ModelData(
                baseShape: new CircleBase(0.75f),
                totalWounds: 1,
                remainingWoundsBinding: store.GetDataBinding<float>(woundsRef),
                positionBinding: store.GetDataBinding<Position>(positionRef),
                weapons: new List<Weapon>(),
                id: providedId);

            Assert.That(model.ID, Is.EqualTo(providedId));
        }

        [Test]
        public void ID_SurvivesGameDataStoreRoundTrip()
        {
            // Mirrors UnitIDTests.ID_SurvivesGameDataStoreRoundTrip: serialise from one store,
            // rehydrate in a fresh one, assert the ID matches. Covers both save/load and network
            // transmission since both flow through GameDataStore JSON. A broken ModelID would
            // silently break any beat/state that names a specific model across the wire.

            GameDataStore fromStore = NewStore();

            var model = MakeBareModel(fromStore);
            DataReference modelRef = fromStore.Create(model);
            ModelID originalID = model.ID;

            string serializedWounds   = fromStore.GetValueAsJson<float>(model.RemainingWoundsBinding.Reference);
            string serializedPosition = fromStore.GetValueAsJson<Position>(model.PositionBinding.Reference);
            string serializedModel    = fromStore.GetValueAsJson<ModelData>(modelRef);

            GameDataStore toStore = NewStore();
            toStore.CreateFromReferenceAndJson(model.RemainingWoundsBinding.Reference, serializedWounds);
            toStore.CreateFromReferenceAndJson(model.PositionBinding.Reference, serializedPosition);
            toStore.CreateFromReferenceAndJson(modelRef, serializedModel);

            ModelData deserializedModel = toStore.GetValue<ModelData>(modelRef);

            Assert.That(deserializedModel.ID, Is.EqualTo(originalID),
                "ModelID must survive serialisation round-trip.");
        }

        private static GameDataStore NewStore() =>
            new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(8)
                .RegisterType<Position>(8)
                .RegisterType<ModelData>(8)
                .Build();

        /// <summary>
        /// Constructs a minimal <see cref="ModelData"/> backed by the given store. Sufficient for
        /// tests that only need the model's identity.
        /// </summary>
        private static ModelData MakeBareModel(GameDataStore store) =>
            new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(),
                store);
    }
}
