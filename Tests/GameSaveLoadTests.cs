using FDG.Data;
using FDG.SaveLoad;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class GameSaveLoadTests
    {
        [Test]
        public void FullStore_RoundTrips_PositionsWoundsObjectiveOwnerAndProgress()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { player }));

            // A model with a real position and a non-default remaining-wound count.
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(3, 4), store);
            DataReference modelRef = store.Create(model);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(modelRef);
            model.SetMaxWounds(3);
            model.DealWounds(1); // remaining = 2

            var unit = new UnitData(player, "Grunt", 4, 4, new List<DataBinding<ModelData>> { modelBinding });
            DataReference unitRef = store.Create(unit);
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(unitRef);
            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));

            // An objective owned by the player — exercises the ObjectiveData(12) -> PlayerID(13)
            // forward reference, which only round-trips because replay retries deferred entries.
            var objective = new ObjectiveData(new Position(10, 10), store);
            objective.SetOwner(player);
            store.Create(objective);

            GameProgressUtilities.WriteProgress(store, new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: 2,
                teamActivateOrder: new List<int> { 0 },
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 } },
                unactivatedUnits: new List<DataBinding<UnitData>> { unitBinding },
                settings: GameSettings.GetDefault()));

            string json = GameSaveSerializer.Save(store);
            GameDataStore loaded = GameSaveSerializer.Load(json);

            ModelData lModel = loaded.GetValue<ModelData>(modelRef);
            Assert.That(lModel.Position.x, Is.EqualTo(3f));
            Assert.That(lModel.Position.z, Is.EqualTo(4f));
            Assert.That(lModel.RemainingWoundsBinding.GetValue(), Is.EqualTo(2f));

            List<ObjectiveData> lObjectives = loaded.GetAllValues<ObjectiveData>().ToList();
            Assert.That(lObjectives.Count, Is.EqualTo(1));
            Assert.That(lObjectives[0].OwnerID, Is.EqualTo(player));

            GameProgressData? lProgress = GameProgressUtilities.TryGetProgress(loaded);
            Assert.That(lProgress, Is.Not.Null);
            Assert.That(lProgress!.RoundCount, Is.EqualTo(2));
            Assert.That(lProgress.UnactivatedUnits.Count, Is.EqualTo(1));
            Assert.That(lProgress.UnactivatedUnits[0].GetValue().Name, Is.EqualTo("Grunt"));
        }

        [Test]
        public void PostLoad_UnitWoundEvent_FiresForRestoredUnit()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());

            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1, 1), store);
            DataReference modelRef = store.Create(model);
            model.SetMaxWounds(3);
            var unit = new UnitData(player, "Grunt", 4, 4, new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(modelRef) });
            DataReference unitRef = store.Create(unit);

            string json = GameSaveSerializer.Save(store);
            GameDataStore loaded = GameSaveSerializer.Load(json);

            // Without the post-load re-wire, the unit's aggregate event would never fire — the
            // [JsonConstructor] doesn't subscribe to its models.
            UnitData lUnit = loaded.GetValue<UnitData>(unitRef);
            bool fired = false;
            lUnit.OnWoundsDealt += (_, _) => fired = true;

            loaded.GetValue<ModelData>(modelRef).DealWounds(1);

            Assert.That(fired, Is.True, "Restored unit's OnWoundsDealt should fire after re-wire.");
        }

        [Test]
        public void TypeMap_RecordsCapacitiesAndIsRebuilt()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            string json = GameSaveSerializer.Save(store);

            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(json)!;
            // GameProgressData was registered with capacity 2 in GetDefault().
            SavedTypeEntry? progressType = file.TypeMap
                .FirstOrDefault(t => t.FullName == typeof(GameProgressData).FullName);
            Assert.That(progressType, Is.Not.Null);
            Assert.That(progressType!.Capacity, Is.EqualTo(2));

            // An empty default store still rebuilds cleanly.
            Assert.DoesNotThrow(() => GameSaveSerializer.Load(json));
        }

        [Test]
        public void Load_WrongVersion_Throws()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(GameSaveSerializer.Save(store))!;
            file.Version = 999;
            string tampered = JsonConvert.SerializeObject(file);

            Assert.Throws<InvalidOperationException>(() => GameSaveSerializer.Load(tampered));
        }

        [Test]
        public void Load_UnknownType_Throws()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(GameSaveSerializer.Save(store))!;
            file.TypeMap.Add(new SavedTypeEntry("FDG.ThisTypeDoesNotExist", 4));
            string tampered = JsonConvert.SerializeObject(file);

            Assert.Throws<InvalidOperationException>(() => GameSaveSerializer.Load(tampered));
        }
    }
}
