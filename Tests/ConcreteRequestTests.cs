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

        [Test]
        public async Task StringSelectionRequest_ResolvesCorrectly()
        {
            var registry = new StageResolverRegistry();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());

            var validOptions = new List<string> { "Attack", "Defend", "Move" };
            var invalidOptions = new List<StringSelectionRequest.InvalidOption>
            {
                new StringSelectionRequest.InvalidOption("Retreat", "Unit is too brave to retreat")
            };

            var request = new StringSelectionRequest(
                playerID,
                taskID,
                "Choose your action",
                validOptions,
                invalidOptions);

            var expectedChoice = "Attack";
            var resolver = new TestStringSelectionResolver(expectedChoice);

            registry.RegisterResolver<StringSelectionRequest, string>(resolver);
            var result = await registry.ResolveRequest<StringSelectionRequest, string>(request);

            Assert.That(result, Is.EqualTo(expectedChoice));
        }

        [Test]
        public void StringSelectionRequest_SerializesAndDeserializesCorrectly()
        {
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());

            var validOptions = new List<string> { "Attack", "Defend", "Move" };
            var invalidOptions = new List<StringSelectionRequest.InvalidOption>
            {
                new StringSelectionRequest.InvalidOption("Retreat", "Unit is too brave to retreat")
            };

            var request = new StringSelectionRequest(
                playerID,
                taskID,
                "Choose your action",
                validOptions,
                invalidOptions);

            string serialized = JsonConvert.SerializeObject(request);
            var deserialized = JsonConvert.DeserializeObject<StringSelectionRequest>(serialized);

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.ValidOptions.Count, Is.EqualTo(3));
            Assert.That(deserialized.InvalidOptions.Count, Is.EqualTo(1));
            Assert.That(deserialized.ValidOptions[0], Is.EqualTo("Attack"));
            Assert.That(deserialized.ValidOptions[1], Is.EqualTo("Defend"));
            Assert.That(deserialized.ValidOptions[2], Is.EqualTo("Move"));
            Assert.That(deserialized.InvalidOptions[0].Option, Is.EqualTo("Retreat"));
            Assert.That(deserialized.InvalidOptions[0].Reason, Is.EqualTo("Unit is too brave to retreat"));
            Assert.That(deserialized.Instructions, Is.EqualTo("Choose your action"));
        }

        [Test]
        public async Task SingleBindingRequest_ResolvesCorrectly()
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

            // Create test data
            var model = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStore);
            var ref1 = gameDataStore.Create(model);
            var binding = gameDataStore.GetDataBinding<ModelData>(ref1);

            var request = new SingleBindingRequest<ModelData>(
                playerID,
                taskID,
                "Select your army");

            var resolver = new TestSingleBindingResolver<ModelData>(binding);

            // Act
            registry.RegisterResolver<SingleBindingRequest<ModelData>, DataBinding<ModelData>>(resolver);
            var result = await registry.ResolveRequest<SingleBindingRequest<ModelData>, DataBinding<ModelData>>(request);

            // Assert
            Assert.That(result, Is.EqualTo(binding));
            Assert.That(result.GetValue(), Is.EqualTo(model));
        }

        [Test]
        public void SingleBindingRequest_SerializesAndDeserializesCorrectly()
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
            var model = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreFrom);
            var ref1 = gameDataStoreFrom.Create(model);
            var binding = gameDataStoreFrom.GetDataBinding<ModelData>(ref1);

            var request = new SingleBindingRequest<ModelData>(
                new PlayerID(Guid.NewGuid()),
                new TaskID(Guid.NewGuid()),
                "Select your army");

            // Create the same data in the target store
            var modelTo = new ModelData(1.0f, new List<Weapon>(), new List<SpecialRule>(), new Position(), gameDataStoreTo);
            gameDataStoreTo.CreateFromReferenceAndJson(ref1, gameDataStoreFrom.GetValueAsJson<ModelData>(ref1));

            // Act
            var converterFrom = new DataBindingJsonConverter<ModelData>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<ModelData>(gameDataStoreTo);

            string serialized = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { converterFrom }
            });

            var deserialized = JsonConvert.DeserializeObject<SingleBindingRequest<ModelData>>(serialized, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { converterTo }
            });

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Instructions, Is.EqualTo("Select your army"));
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

        private class TestStringSelectionResolver : IStageResolver<StringSelectionRequest, string>
        {
            private readonly string _expectedResponse;

            public TestStringSelectionResolver(string expectedResponse)
            {
                _expectedResponse = expectedResponse;
            }

            public Task<string> Resolve(StringSelectionRequest request)
            {
                return Task.FromResult(_expectedResponse);
            }
        }

        private class TestSingleBindingResolver<T> : IStageResolver<SingleBindingRequest<T>, DataBinding<T>>
        {
            private readonly DataBinding<T> _expectedResponse;

            public TestSingleBindingResolver(DataBinding<T> expectedResponse)
            {
                _expectedResponse = expectedResponse;
            }

            public Task<DataBinding<T>> Resolve(SingleBindingRequest<T> request)
            {
                return Task.FromResult(_expectedResponse);
            }
        }

        #endregion

    }
}