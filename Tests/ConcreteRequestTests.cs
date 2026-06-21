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
            var model1 = new ModelData(0.75f, new List<Weapon>(), new Position(), gameDataStore);
            var model2 = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStore);
            var model3 = new ModelData(1.25f, new List<Weapon>(), new Position(), gameDataStore);

            var ref1 = gameDataStore.Create(model1);
            var ref2 = gameDataStore.Create(model2);
            var ref3 = gameDataStore.Create(model3);

            var binding1 = gameDataStore.GetDataBinding<ModelData>(ref1);
            var binding2 = gameDataStore.GetDataBinding<ModelData>(ref2);
            var binding3 = gameDataStore.GetDataBinding<ModelData>(ref3);

            var validOptions = new List<SelectionRequest<ModelData>.ValidOption>
            { 
                new SelectionRequest<ModelData>.ValidOption(binding1, "Binding 1"), 
                new SelectionRequest<ModelData>.ValidOption(binding2, "Binding 2")
            };

            var invalidOptions = new List<SelectionRequest<ModelData>.InvalidOption>()
            {  new SelectionRequest<ModelData>.InvalidOption(binding3, "Binding 3", "This model is too large")  };

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
            var model1 = new ModelData(0.75f, new List<Weapon>(), new Position(), gameDataStoreFrom);
            var model2 = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStoreFrom);
            var model3 = new ModelData(1.25f, new List<Weapon>(), new Position(), gameDataStoreFrom);

            var ref1 = gameDataStoreFrom.Create(model1);
            var ref2 = gameDataStoreFrom.Create(model2);
            var ref3 = gameDataStoreFrom.Create(model3);

            var binding1 = gameDataStoreFrom.GetDataBinding<ModelData>(ref1);
            var binding2 = gameDataStoreFrom.GetDataBinding<ModelData>(ref2);
            var binding3 = gameDataStoreFrom.GetDataBinding<ModelData>(ref3);

            var validOptions = new List<SelectionRequest<ModelData>.ValidOption>
            {
                new SelectionRequest<ModelData>.ValidOption(binding1, "Binding 1"),
                new SelectionRequest<ModelData>.ValidOption(binding2, "Binding 2")
            };
            var invalidOptions = new List<SelectionRequest<ModelData>.InvalidOption>
            {
                new SelectionRequest<ModelData>.InvalidOption(binding3, "Binding 3", "This model is too large")
            };

            var request = new SelectionRequest<ModelData>(
                new PlayerID(Guid.NewGuid()),
                new TaskID(Guid.NewGuid()),
                "Select a model to move",
                validOptions,
                invalidOptions);

            // Create the same data in the target store
            var model1To = new ModelData(0.75f, new List<Weapon>(), new Position(), gameDataStoreTo);
            var model2To = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStoreTo);
            var model3To = new ModelData(1.25f, new List<Weapon>(), new Position(), gameDataStoreTo);

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
            Assert.That(deserializedBinding1.Option.GetValue().BaseRadiusInches, Is.EqualTo(model1To.BaseRadiusInches));
            Assert.That(deserializedBinding2.Option.GetValue().BaseRadiusInches, Is.EqualTo(model2To.BaseRadiusInches));

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
            var model = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStore);
            var ref1 = gameDataStore.Create(model);
            var binding = gameDataStore.GetDataBinding<ModelData>(ref1);

            var request = new SingleBindingRequest<ModelData>(
                playerID,
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
            var model = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStoreFrom);
            var ref1 = gameDataStoreFrom.Create(model);
            var binding = gameDataStoreFrom.GetDataBinding<ModelData>(ref1);

            var request = new SingleBindingRequest<ModelData>(
                new PlayerID(Guid.NewGuid()),
                "Select your army");

            // Create the same data in the target store
            var modelTo = new ModelData(1.0f, new List<Weapon>(), new Position(), gameDataStoreTo);
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

        [Test]
        public void ChooseRangedAttackRequest_SerializesAndDeserializesCorrectly_SameStore()
        {
            // Single-store variant: confirms Newtonsoft.Json can round-trip
            // WeaponTargetStats (record) with HashSet<DataBinding<ModelData>>.
            var store    = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());

            var attackerModel   = new ModelData(0.75f,
                new List<Weapon> { new Weapon("Rifle", 24f, 1, 0) },
                new Position(), store);
            var attackerModelRef     = store.Create(attackerModel);
            var attackerModelBinding = store.GetDataBinding<ModelData>(attackerModelRef);

            var attackerUnit    = new UnitData(playerID, "Attacker", 3, 3, new List<DataBinding<ModelData>> { attackerModelBinding });
            var attackerUnitRef     = store.Create(attackerUnit);
            var attackerUnitBinding = store.GetDataBinding<UnitData>(attackerUnitRef);

            var targetModel   = new ModelData(0.75f, new List<Weapon>(), new Position(), store);
            var targetModelRef     = store.Create(targetModel);
            var targetModelBinding = store.GetDataBinding<ModelData>(targetModelRef);

            var targetUnit    = new UnitData(playerID, "Target", 3, 3, new List<DataBinding<ModelData>> { targetModelBinding });
            var targetUnitRef     = store.Create(targetUnit);
            var targetUnitBinding = store.GetDataBinding<UnitData>(targetUnitRef);

            var canShoot    = new HashSet<DataBinding<ModelData>> { attackerModelBinding };
            var cannotShoot = new HashSet<DataBinding<ModelData>>();
            var targetStats = new ChooseRangedAttackRequest.WeaponTargetStats(
                targetUnitBinding, canShoot, cannotShoot, false);
            var weapon      = new Weapon("Rifle", 24f, 1, 0);
            var weaponOpt   = new ChooseRangedAttackRequest.WeaponOption(
                weapon, new List<ChooseRangedAttackRequest.WeaponTargetStats> { targetStats });
            var request     = new ChooseRangedAttackRequest(playerID, "Choose Ranged Weapon",
                attackerUnitBinding,
                new List<ChooseRangedAttackRequest.WeaponOption> { weaponOpt });

            string json        = Newtonsoft.Json.JsonConvert.SerializeObject(request, store.GetJsonSettings());
            var    deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(json, store.GetJsonSettings());

            Assert.That(deserialized,                                                         Is.Not.Null);
            Assert.That(deserialized.WeaponOptions,                                           Is.Not.Null);
            Assert.That(deserialized.WeaponOptions.Count,                                     Is.EqualTo(1));
            Assert.That(deserialized.WeaponOptions[0].Weapon.Name,                            Is.EqualTo("Rifle"));
            Assert.That(deserialized.WeaponOptions[0].WeaponTargetStats.Count,                Is.EqualTo(1));
            Assert.That(deserialized.WeaponOptions[0].WeaponTargetStats[0].modelsThatCanShoot.Count, Is.EqualTo(1));
            Assert.That(deserialized.AttackingUnit.GetValue().Name,                           Is.EqualTo("Attacker"));
        }

        [Test]
        public void ChooseRangedAttackRequest_SerializesAndDeserializesCorrectly_CrossStore()
        {
            // Two-store variant: simulates multiplayer where the client store is
            // pre-populated via OnDataAddedAsJson (same way the real game works).
            var hostStore   = GameDataStore.GameDataStoreBuilder.GetDefault();
            var clientStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            // Wire replication: host → client (mirrors how GameDataUpdateSender works)
            hostStore.OnDataAddedAsJson += (reference, json) =>
                clientStore.CreateFromReferenceAndJson(reference, json);
            hostStore.OnDataUpdatedAsJson += (reference, json) =>
                clientStore.SetValueWithJson(reference, json);

            var playerID = new PlayerID(Guid.NewGuid());

            // Create data on host — replication fires automatically via events
            var attackerModel   = new ModelData(0.75f,
                new List<Weapon> { new Weapon("Rifle", 24f, 1, 0) },
                new Position(), hostStore);
            var attackerModelRef     = hostStore.Create(attackerModel);
            var attackerModelBinding = hostStore.GetDataBinding<ModelData>(attackerModelRef);

            var attackerUnit    = new UnitData(playerID, "Attacker", 3, 3, new List<DataBinding<ModelData>> { attackerModelBinding });
            var attackerUnitRef     = hostStore.Create(attackerUnit);
            var attackerUnitBinding = hostStore.GetDataBinding<UnitData>(attackerUnitRef);

            var targetModel   = new ModelData(0.75f, new List<Weapon>(), new Position(), hostStore);
            var targetModelRef     = hostStore.Create(targetModel);
            var targetModelBinding = hostStore.GetDataBinding<ModelData>(targetModelRef);

            var targetUnit    = new UnitData(playerID, "Target", 3, 3, new List<DataBinding<ModelData>> { targetModelBinding });
            var targetUnitRef     = hostStore.Create(targetUnit);
            var targetUnitBinding = hostStore.GetDataBinding<UnitData>(targetUnitRef);

            // Build request on host side
            var canShoot    = new HashSet<DataBinding<ModelData>> { attackerModelBinding };
            var cannotShoot = new HashSet<DataBinding<ModelData>>();
            var targetStats = new ChooseRangedAttackRequest.WeaponTargetStats(
                targetUnitBinding, canShoot, cannotShoot, false);
            var weapon      = new Weapon("Rifle", 24f, 1, 0);
            var weaponOpt   = new ChooseRangedAttackRequest.WeaponOption(
                weapon, new List<ChooseRangedAttackRequest.WeaponTargetStats> { targetStats });
            var request     = new ChooseRangedAttackRequest(playerID, "Choose Ranged Weapon",
                attackerUnitBinding,
                new List<ChooseRangedAttackRequest.WeaponOption> { weaponOpt });

            // Serialize with host settings, deserialize with client settings
            string json        = Newtonsoft.Json.JsonConvert.SerializeObject(request, hostStore.GetJsonSettings());
            var    deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(json, clientStore.GetJsonSettings());

            Assert.That(deserialized,                                                              Is.Not.Null);
            Assert.That(deserialized.WeaponOptions,                                                Is.Not.Null);
            Assert.That(deserialized.WeaponOptions.Count,                                          Is.EqualTo(1));
            Assert.That(deserialized.WeaponOptions[0].Weapon.Name,                                 Is.EqualTo("Rifle"));
            Assert.That(deserialized.WeaponOptions[0].WeaponTargetStats.Count,                     Is.EqualTo(1));
            Assert.That(deserialized.WeaponOptions[0].WeaponTargetStats[0].modelsThatCanShoot.Count, Is.EqualTo(1));
            Assert.That(deserialized.AttackingUnit.GetValue().Name,                                Is.EqualTo("Attacker"));
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

        #region PlaceObjectsRequest zone round-trip (#035 genericized IBoundedZone)

        // The placement zone is carried inline (polymorphic IBoundedZone), not as a store reference, so it
        // must survive the wire as its concrete shape. A circular disembark zone is the new case.
        [Test]
        public void PlaceObjectsRequest_CircularZone_RoundTrips_CrossStore()
        {
            var hostStore   = GameDataStore.GameDataStoreBuilder.GetDefault();
            var clientStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            hostStore.OnDataAddedAsJson   += (reference, json) => clientStore.CreateFromReferenceAndJson(reference, json);
            hostStore.OnDataUpdatedAsJson += (reference, json) => clientStore.SetValueWithJson(reference, json);

            var playerID = new PlayerID(Guid.NewGuid());
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(), hostStore);
            var modelBinding = hostStore.GetDataBinding<ModelData>(hostStore.Create(model));

            var request = new PlaceObjectsRequest<ModelData>(playerID, "Disembark",
                new CircularZone(new Float2(10f, 5f), 6f),
                new List<DataBinding<ModelData>> { modelBinding });

            string json = JsonConvert.SerializeObject(request, hostStore.GetJsonSettings());
            var deserialized = JsonConvert.DeserializeObject<PlaceObjectsRequest<ModelData>>(json, clientStore.GetJsonSettings());

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.DeploymentZone, Is.TypeOf<CircularZone>(),
                "the polymorphic zone survives the wire as its concrete circular type.");
            var zone = (CircularZone)deserialized.DeploymentZone;
            Assert.That(zone.Radius, Is.EqualTo(6f).Within(0.001f));
            Assert.That(zone.Center.X, Is.EqualTo(10f).Within(0.001f));
            Assert.That(zone.Center.Y, Is.EqualTo(5f).Within(0.001f));
            Assert.That(deserialized.DeploymentZone.IsPointWithinZone(new Float2(13f, 5f)), Is.True,  "3\" from centre is inside.");
            Assert.That(deserialized.DeploymentZone.IsPointWithinZone(new Float2(20f, 5f)), Is.False, "10\" from centre is outside.");
            Assert.That(deserialized.ModelsToPlace.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlaceObjectsRequest_RectangularZone_RoundTrips_CrossStore()
        {
            var hostStore   = GameDataStore.GameDataStoreBuilder.GetDefault();
            var clientStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            hostStore.OnDataAddedAsJson   += (reference, json) => clientStore.CreateFromReferenceAndJson(reference, json);
            hostStore.OnDataUpdatedAsJson += (reference, json) => clientStore.SetValueWithJson(reference, json);

            var playerID = new PlayerID(Guid.NewGuid());
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(), hostStore);
            var modelBinding = hostStore.GetDataBinding<ModelData>(hostStore.Create(model));

            var request = new PlaceObjectsRequest<ModelData>(playerID, "Place Unit Models",
                new RectangularZone(0f, 24f, 0f, 12f),
                new List<DataBinding<ModelData>> { modelBinding });

            string json = JsonConvert.SerializeObject(request, hostStore.GetJsonSettings());
            var deserialized = JsonConvert.DeserializeObject<PlaceObjectsRequest<ModelData>>(json, clientStore.GetJsonSettings());

            Assert.That(deserialized!.DeploymentZone, Is.TypeOf<RectangularZone>());
            ZoneBounds b = deserialized.DeploymentZone.Bounds;
            Assert.That(b.Left, Is.EqualTo(0f).Within(0.001f));
            Assert.That(b.Right, Is.EqualTo(24f).Within(0.001f));
            Assert.That(b.Top, Is.EqualTo(12f).Within(0.001f));
        }

        #endregion

    }
}