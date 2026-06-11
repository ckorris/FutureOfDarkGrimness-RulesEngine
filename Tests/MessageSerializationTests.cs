using FDG.Data;
using FDG.Data.Serialization;
using Newtonsoft.Json;
using NUnit.Framework;
using System;

namespace FDG.Tests
{
    [TestFixture]
    public class MessageSerializationTests
    {

        [Test]
        public void DataBindingDeserializeTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            //These should produce DataReferences that are identical.
            DataReference referenceFrom = gameDataStoreFrom.Create(5);
            DataReference referenceTo = gameDataStoreTo.Create(5);

            Assert.That(referenceFrom, Is.EqualTo(referenceTo));

            DataBinding<int> fromBinding = gameDataStoreFrom.GetDataBinding<int>(referenceFrom);
            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(referenceTo);

            var converterFrom = new DataBindingJsonConverter<int>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<int>(gameDataStoreTo);

            string fromSerialized = JsonConvert.SerializeObject(fromBinding, [converterFrom]);

            DataBinding<int>? toDeserialized = JsonConvert.DeserializeObject<DataBinding<int>>(fromSerialized, [converterTo]);

            Assert.That(toDeserialized, Is.EqualTo(toBinding));
        }

        [Test]
        public void DataBindingFieldDeserializeTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            DataReference referenceFrom = gameDataStoreFrom.Create(5);
            DataReference referenceTo = gameDataStoreTo.Create(5);


            TestMessageWithIntBinding fromMessage = new TestMessageWithIntBinding(referenceFrom, gameDataStoreFrom);

            var converterFrom = new DataBindingJsonConverter<int>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<int>(gameDataStoreTo);

            string fromSerialized = JsonConvert.SerializeObject(fromMessage, [converterFrom]);

            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(referenceTo);

            TestMessageWithIntBinding? toDeserialized = 
                JsonConvert.DeserializeObject<TestMessageWithIntBinding>(fromSerialized, [converterTo]);

            Assert.That(toDeserialized?.IntValueBinding, Is.EqualTo(toBinding));
        }

        [Test]
        public void DataBindingFieldDeserializeInGameDataStoreTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(1)
                .RegisterType<TestMessageWithIntBinding>(1)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(1)
                .RegisterType<TestMessageWithIntBinding>(1)
                .Build();

            DataReference intReferenceFrom = gameDataStoreFrom.Create(5);
            DataReference intReferenceTo = gameDataStoreTo.Create(5);

            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(intReferenceTo);

            TestMessageWithIntBinding fromMessage = new TestMessageWithIntBinding(intReferenceFrom, gameDataStoreFrom);
            DataReference messageReferenceFrom = gameDataStoreFrom.Create(fromMessage);

            string messageAsJson = gameDataStoreFrom.GetValueAsJson<TestMessageWithIntBinding>(messageReferenceFrom);

            Assert.That(gameDataStoreTo.IsValid(messageReferenceFrom, out _), Is.False);

            gameDataStoreTo.CreateFromReferenceAndJson(messageReferenceFrom, messageAsJson);

            Assert.That(gameDataStoreTo.IsValid(messageReferenceFrom, out _), Is.True);

            TestMessageWithIntBinding toMessage = gameDataStoreTo.GetValue<TestMessageWithIntBinding>(messageReferenceFrom);

            Assert.That(toMessage.IntValueBinding?.IsValid, Is.True);
            Assert.That(toMessage.IntValueBinding?.GetValue(), Is.EqualTo(5));
        }

        [Test]
        public void ModelDataSerializationRoundTripTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .Build();

            float baseRadiusInches = 0.75f;
            List<Weapon> weapons = new List<Weapon>() { new Weapon("Weapon 1", 6, 2, 1) };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Hero() };
            Position position = new Position(new Float3(1, 2, 3));

            ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, gameDataStoreFrom);

            DataReference modelDataReference = gameDataStoreFrom.Create(modelData);

            ModelData retrievedLocalModelData = gameDataStoreFrom.GetValue<ModelData>(modelDataReference);
            Assert.That(retrievedLocalModelData, Is.EqualTo(modelData));

            DataReference floatDataReference = retrievedLocalModelData.RemainingWoundsBinding.Reference;
            DataReference positionDataReference = retrievedLocalModelData.PositionBinding.Reference;

            string serializedFloatData = gameDataStoreFrom.GetValueAsJson<float>(floatDataReference);
            string serializedPositionData = gameDataStoreFrom.GetValueAsJson<Position>(positionDataReference);
            string serializedModelData = gameDataStoreFrom.GetValueAsJson<ModelData>(modelDataReference);

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .Build();

            gameDataStoreTo.CreateFromReferenceAndJson(floatDataReference, serializedFloatData);
            gameDataStoreTo.CreateFromReferenceAndJson(positionDataReference, serializedPositionData);
            gameDataStoreTo.CreateFromReferenceAndJson(modelDataReference, serializedModelData);

            ModelData deserializedModelData = gameDataStoreTo.GetValue<ModelData>(modelDataReference);
            Assert.That(deserializedModelData.RemainingWoundsBinding.Reference, Is.EqualTo(floatDataReference));
            Assert.That(deserializedModelData.RemainingWoundsBinding.GetValue(), Is.EqualTo(modelData.RemainingWoundsBinding.GetValue()));
            Assert.That(deserializedModelData.PositionBinding.Reference, Is.EqualTo(positionDataReference));
            Assert.That(deserializedModelData.PositionBinding.GetValue(), Is.EqualTo(modelData.PositionBinding.GetValue()));
        }

        [Test]
        public void UnitDataSerializationRoundTripTest()
        {
            // Build the source GameDataStore registering the needed types.
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                 .RegisterType<float>(1)
                 .RegisterType<Position>(1)
                 .RegisterType<ModelData>(1)
                 .RegisterType<UnitData>(1)
                 .Build();

            // Create a dummy ModelData needed by UnitData.
            float baseRadius = 0.75f;
            List<Weapon> weapons = new List<Weapon>()
        {
            new Weapon("DummyWeapon", 6, 2, 1)
        };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Hero() };
            Position position = new Position(new Float3(1, 2, 3));

            // Create ModelData via the live constructor.
            ModelData modelData = new ModelData(baseRadius, weapons, specialRules, position, gameDataStoreFrom);
            DataReference modelDataReference = gameDataStoreFrom.Create(modelData);
            DataBinding<ModelData> modelBinding = gameDataStoreFrom.GetDataBinding<ModelData>(modelDataReference);

            // UnitData expects a list of model references.
            List<DataReference> modelReferences = new List<DataReference> { modelDataReference };

            // Create UnitData using the template constructor.
            UnitData unitData = new UnitData(new PlayerID(Guid.NewGuid()), "Test Unit", 5, 4, specialRules, 
                new List<DataBinding<ModelData>>() { modelBinding });
            DataReference unitDataReference = gameDataStoreFrom.Create(unitData);

            // Retrieve locally and serialize.
            UnitData retrievedLocalUnitData = gameDataStoreFrom.GetValue<UnitData>(unitDataReference);

            string serializedWoundsData = gameDataStoreFrom.GetValueAsJson<float>(modelData.RemainingWoundsBinding.Reference);
            string serializedPositionData = gameDataStoreFrom.GetValueAsJson<Position>(modelData.PositionBinding.Reference);
            string serializedModelData = gameDataStoreFrom.GetValueAsJson<ModelData>(modelDataReference);
            string serializedUnitData = gameDataStoreFrom.GetValueAsJson<UnitData>(unitDataReference);

            // Build a new GameDataStore and rehydrate.
            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                 .RegisterType<float>(1)
                 .RegisterType<Position>(1)
                 .RegisterType<ModelData>(1)
                 .RegisterType<UnitData>(1)
                 .Build();

            gameDataStoreTo.CreateFromReferenceAndJson(modelData.RemainingWoundsBinding.Reference, serializedWoundsData);
            gameDataStoreTo.CreateFromReferenceAndJson(modelData.PositionBinding.Reference, serializedPositionData);
            gameDataStoreTo.CreateFromReferenceAndJson(modelDataReference, serializedModelData);
            gameDataStoreTo.CreateFromReferenceAndJson(unitDataReference, serializedUnitData);

            UnitData deserializedUnitData = gameDataStoreTo.GetValue<UnitData>(unitDataReference);

            // Assert that the model binding reference and value are preserved.
            DataReference retrievedModelBindingReference = deserializedUnitData.ModelBindings.First().Reference;
            Assert.That(retrievedModelBindingReference, Is.EqualTo(modelDataReference));

            ModelData retrievedModelFromUnit = deserializedUnitData.ModelBindings.First().GetValue();
            ModelData originalModel = gameDataStoreFrom.GetValue<ModelData>(modelDataReference);
            Assert.That(retrievedModelFromUnit.BaseRadiusInches, Is.EqualTo(originalModel.BaseRadiusInches));
            Assert.That(retrievedModelFromUnit.RemainingWoundsBinding.Reference, Is.EqualTo(originalModel.RemainingWoundsBinding.Reference));
            Assert.That(retrievedModelFromUnit.RemainingWoundsBinding.GetValue(), Is.EqualTo(originalModel.RemainingWoundsBinding.GetValue()));
            Assert.That(retrievedModelFromUnit.PositionBinding.Reference, Is.EqualTo(originalModel.PositionBinding.Reference));
            Assert.That(retrievedModelFromUnit.PositionBinding.GetValue(), Is.EqualTo(originalModel.PositionBinding.GetValue()));

            // Assert basic properties.
            Assert.That(deserializedUnitData.Name, Is.EqualTo(unitData.Name));
            Assert.That(deserializedUnitData.Quality, Is.EqualTo(unitData.Quality));
            Assert.That(deserializedUnitData.Defense, Is.EqualTo(unitData.Defense));
        }

        [Test]
        public void ArmyDataSerializationRoundTripTest()
        {
            // Build the source GameDataStore with required types.
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                 .RegisterType<float>(1)
                 .RegisterType<Position>(1)
                 .RegisterType<ModelData>(1)
                 .RegisterType<UnitData>(1)
                 .RegisterType<ArmyData>(1)
                 .Build();

            // Create a dummy ModelData (as above) for the UnitData.
            float baseRadius = 0.75f;
            List<Weapon> weapons = new List<Weapon>()
        {
            new Weapon("DummyWeapon", 6, 2, 1)
        };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Hero() };
            Position position = new Position(new Float3(1, 2, 3));
            ModelData modelData = new ModelData(baseRadius, weapons, specialRules, position, gameDataStoreFrom);
            DataReference modelDataReference = gameDataStoreFrom.Create(modelData);
            DataBinding<ModelData> modelBinding = gameDataStoreFrom.GetDataBinding<ModelData>(modelDataReference);

            List<DataReference> modelReferences = new List<DataReference> { modelDataReference };
            UnitData unitData = new UnitData(new PlayerID(Guid.NewGuid()), "Test Model", 4, 4, specialRules,
                new List<DataBinding<ModelData>>() { modelBinding });
            DataReference unitDataReference = gameDataStoreFrom.Create(unitData);
            DataBinding<UnitData> unitBinding = gameDataStoreFrom.GetDataBinding<UnitData>(unitDataReference);

            // Create ArmyData using the live constructor.
            ArmyData armyData = new ArmyData(unitData.PlayerID, new List<DataBinding<UnitData>>() { unitBinding });
            DataReference armyDataReference = gameDataStoreFrom.Create(armyData);

            // Retrieve and serialize.
            ArmyData retrievedLocalArmyData = gameDataStoreFrom.GetValue<ArmyData>(armyDataReference);

            string serializedWoundsData = gameDataStoreFrom.GetValueAsJson<float>(modelData.RemainingWoundsBinding.Reference);
            string serializedPositionData = gameDataStoreFrom.GetValueAsJson<Position>(modelData.PositionBinding.Reference);
            string serializedModelData = gameDataStoreFrom.GetValueAsJson<ModelData>(modelDataReference);
            string serializedUnitData = gameDataStoreFrom.GetValueAsJson<UnitData>(unitDataReference);
            string serializedArmyData = gameDataStoreFrom.GetValueAsJson<ArmyData>(armyDataReference);

            // Build a new store and rehydrate.
            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                 .RegisterType<float>(1)
                 .RegisterType<Position>(1)
                 .RegisterType<ModelData>(1)
                 .RegisterType<UnitData>(1)
                 .RegisterType<ArmyData>(1)
                 .Build();

            gameDataStoreTo.CreateFromReferenceAndJson(modelData.RemainingWoundsBinding.Reference, serializedWoundsData);
            gameDataStoreTo.CreateFromReferenceAndJson(modelData.PositionBinding.Reference, serializedPositionData);
            gameDataStoreTo.CreateFromReferenceAndJson(modelDataReference, serializedModelData);
            gameDataStoreTo.CreateFromReferenceAndJson(unitDataReference, serializedUnitData);
            gameDataStoreTo.CreateFromReferenceAndJson(armyDataReference, serializedArmyData);

            ArmyData deserializedArmyData = gameDataStoreTo.GetValue<ArmyData>(armyDataReference);

            // Assert that the unit binding references and the contained UnitData values are preserved.
            Assert.That(deserializedArmyData.UnitBindings.Count, Is.EqualTo(retrievedLocalArmyData.UnitBindings.Count));
            for (int i = 0; i < deserializedArmyData.UnitBindings.Count; i++)
            {
                DataReference originalRef = retrievedLocalArmyData.UnitBindings[i].Reference;
                DataReference deserializedRef = deserializedArmyData.UnitBindings[i].Reference;
                Assert.That(deserializedRef, Is.EqualTo(originalRef));

                UnitData originalUnit = retrievedLocalArmyData.UnitBindings[i].GetValue();
                UnitData deserializedUnit = deserializedArmyData.UnitBindings[i].GetValue();
                Assert.That(deserializedUnit.Name, Is.EqualTo(originalUnit.Name));
                Assert.That(deserializedUnit.Quality, Is.EqualTo(originalUnit.Quality));
                Assert.That(deserializedUnit.Defense, Is.EqualTo(originalUnit.Defense));
                
                List<DataReference> originalModelBindings = originalUnit.ModelBindings.Select(model => model.Reference).ToList();
                List<DataReference> deserializedModelBindings = deserializedUnit.ModelBindings.Select(model => model.Reference).ToList();

                Assert.That(originalModelBindings.Count, Is.EqualTo(deserializedModelBindings.Count));
                for(int j = 0; j < originalModelBindings.Count; j++)
                {
                    Assert.That(originalModelBindings[i], Is.EqualTo(deserializedModelBindings[i]));
                }
            }
        }

        public class TestMessageWithIntBinding
        {
            public DataBinding<int>? IntValueBinding;

            
            [JsonConstructor]
            public TestMessageWithIntBinding(DataBinding<int> intValueBinding)
            {
                IntValueBinding = intValueBinding;
            }
            

            public TestMessageWithIntBinding(DataReference intValueReference, 
                IReadWriteableGameDataStore gameDataStore)
            {
                IntValueBinding = gameDataStore.GetDataBinding<int>(intValueReference);
            }
        }

    }
}
