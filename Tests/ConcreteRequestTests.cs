using FDG.Data;
using FDG.Data.Serialization;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FDG.Tests
{
    [TestFixture]
    public class ConcreteRequestTests
    {


        [Test]
        public async Task YesNoRequest_ResolvesCorrectly()
        {
            // Arrange
            var registry = new StageResolverRegistry();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());
            var expectedResponse = true;
            var resolver = new TestYesNoResolver(expectedResponse);
            var request = new YesNoRequest(playerID, taskID, "Test Question");

            // Act
            registry.RegisterResolver<YesNoRequest, bool>(resolver);
            var result = await registry.ResolveRequest<YesNoRequest, bool>(request);

            // Assert
            Assert.That(result, Is.EqualTo(expectedResponse));
        }

        [Test]
        public async Task SelectionRequest_ResolvesCorrectly()
        {
            // Arrange
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .Build();

            var registry = new StageResolverRegistry();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());

            // Create some test data
            var model1 = new ModelData(0.75f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStore);
            var model2 = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStore);
            var model3 = new ModelData(1.25f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStore);

            var ref1 = gameDataStore.Create(model1);
            var ref2 = gameDataStore.Create(model2);
            var ref3 = gameDataStore.Create(model3);

            var binding1 = gameDataStore.GetDataBinding<ModelData>(ref1);
            var binding2 = gameDataStore.GetDataBinding<ModelData>(ref2);
            var binding3 = gameDataStore.GetDataBinding<ModelData>(ref3);

            var validOptions = new List<DataBinding<ModelData>> { binding1, binding2 };
            var invalidOptions = new List<SelectionRequest<ModelData>.InvalidOption>()
            {  new SelectionRequest<ModelData>.InvalidOption(binding3, "This model is too large")  };

            var request = new SelectionRequest<ModelData>(
                playerID,
                taskID,
                "Select a model to move",
                validOptions,
                invalidOptions);

            var resolver = new TestSelectionResolver<ModelData>(binding1);

            // Act
            registry.RegisterResolver<SelectionRequest<ModelData>, DataBinding<ModelData>>(resolver);
            var result = await registry.ResolveRequest<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);

            // Assert
            Assert.That(result, Is.EqualTo(binding1));
            Assert.That(result.GetValue(), Is.EqualTo(model1));
        }

        [Test]
        public void SelectionRequest_SerializesAndDeserializesCorrectly()
        {
            // Arrange
            var gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .Build();

            var gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .Build();

            // Create test data in the source store
            var model1 = new ModelData(0.75f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreFrom);
            var model2 = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreFrom);
            var model3 = new ModelData(1.25f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreFrom);

            var ref1 = gameDataStoreFrom.Create(model1);
            var ref2 = gameDataStoreFrom.Create(model2);
            var ref3 = gameDataStoreFrom.Create(model3);

            var binding1 = gameDataStoreFrom.GetDataBinding<ModelData>(ref1);
            var binding2 = gameDataStoreFrom.GetDataBinding<ModelData>(ref2);
            var binding3 = gameDataStoreFrom.GetDataBinding<ModelData>(ref3);

            var validOptions = new List<DataBinding<ModelData>> { binding1, binding2 };
            var invalidOptions = new List<SelectionRequest<ModelData>.InvalidOption>
            {
                new SelectionRequest<ModelData>.InvalidOption(binding3, "This model is too large")
            };

            var request = new SelectionRequest<ModelData>(
                new PlayerID(Guid.NewGuid()),
                new TaskID(Guid.NewGuid()),
                "Select a model to move",
                validOptions,
                invalidOptions);

            // Create the same data in the target store
            var model1To = new ModelData(0.75f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreTo);
            var model2To = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreTo);
            var model3To = new ModelData(1.25f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreTo);

            gameDataStoreTo.CreateFromReferenceAndJson(ref1, gameDataStoreFrom.GetValueAsJson<ModelData>(ref1));
            gameDataStoreTo.CreateFromReferenceAndJson(ref2, gameDataStoreFrom.GetValueAsJson<ModelData>(ref2));
            gameDataStoreTo.CreateFromReferenceAndJson(ref3, gameDataStoreFrom.GetValueAsJson<ModelData>(ref3));

            // Act
            var converterFrom = new DataBindingJsonConverter<ModelData>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<ModelData>(gameDataStoreTo);

            string serialized = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { converterFrom }
            });

            var deserialized = JsonConvert.DeserializeObject<SelectionRequest<ModelData>>(serialized, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { converterTo }
            });

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.ValidOptions.Count, Is.EqualTo(2));
            Assert.That(deserialized.InvalidOptions.Count, Is.EqualTo(1));

            // Verify valid options
            var deserializedBinding1 = deserialized.ValidOptions[0];
            var deserializedBinding2 = deserialized.ValidOptions[1];
            Assert.That(deserializedBinding1.GetValue().BaseRadiusInches, Is.EqualTo(model1To.BaseRadiusInches));
            Assert.That(deserializedBinding2.GetValue().BaseRadiusInches, Is.EqualTo(model2To.BaseRadiusInches));

            // Verify invalid options
            var deserializedInvalidOption = deserialized.InvalidOptions[0];
            Assert.That(deserializedInvalidOption.Option.GetValue().BaseRadiusInches, Is.EqualTo(model3To.BaseRadiusInches));
            Assert.That(deserializedInvalidOption.Reason, Is.EqualTo("This model is too large"));
        }

        #region Test Resolvers

        private class TestYesNoResolver : IStageResolver<YesNoRequest, bool>
        {
            private readonly bool _expectedResponse;

            public TestYesNoResolver(bool expectedResponse)
            {
                _expectedResponse = expectedResponse;
            }

            public Task<bool> Resolve(YesNoRequest request)
            {
                return Task.FromResult(_expectedResponse);
            }
        }

        private class TestSelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>
        {
            private readonly DataBinding<T> _expectedResponse;

            public TestSelectionResolver(DataBinding<T> expectedResponse)
            {
                _expectedResponse = expectedResponse;
            }

            public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
            {
                return Task.FromResult(_expectedResponse);
            }
        }

        #endregion

    }
}