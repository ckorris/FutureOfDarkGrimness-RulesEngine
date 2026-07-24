using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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
            // GameProgressData was registered with capacity 2 in GetDefault(), and recorded by its stable
            // save ID (#070) — the literal "gameProgress" is pinned here because a stable ID must never change.
            SavedTypeEntry? progressType = file.TypeMap
                .FirstOrDefault(t => t.TypeId == "gameProgress");
            Assert.That(progressType, Is.Not.Null, "type map should record GameProgressData by its stable ID.");
            Assert.That(progressType!.Capacity, Is.EqualTo(2));

            // An empty default store still rebuilds cleanly.
            Assert.DoesNotThrow(() => GameSaveSerializer.Load(json));
        }

        // #270 — a slot that was destroyed and refilled during the session carries generation 2, and a
        // store rebuilt from scratch starts every generation at 0. Replay used to reject the entry as
        // FutureGeneration, which made the whole save unloadable. Every resume path recycles slots this
        // way (LobbyViewModel_Host.LaunchResume and ScenarioLauncher both re-crew PlayerSlotInfo), so
        // before this any game that was resumed and then saved again could never be opened.
        [Test]
        public void Store_WithARecycledSlot_StillRoundTrips()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());

            DataReference first = store.Create(new TeamData(0, new List<PlayerID> { player }));
            store.Destroy(first);
            DataReference refilled = store.Create(new TeamData(7, new List<PlayerID> { player }));

            Assert.That(refilled.Index, Is.EqualTo(first.Index), "the slot should be reused");
            Assert.That(refilled.Generation, Is.GreaterThan(first.Generation), "and its generation bumped");

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

            TeamData restored = loaded.GetValue<TeamData>(refilled);
            Assert.That(restored.TeamNumber, Is.EqualTo(7), "the live value survives, at its own generation");
            Assert.That(loaded.GetAllValues<TeamData>().Count(), Is.EqualTo(1), "and the destroyed one stays gone");
        }

        // The same store, saved twice over: the second save must load too, so a session can be resumed,
        // saved, resumed and saved again without ever becoming unopenable.
        [Test]
        public void RecycledSlot_SurvivesRepeatedSaveLoadCycles()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            DataReference slot = store.Create(new TeamData(0, new List<PlayerID> { player }));

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                store.Destroy(slot);
                slot = store.Create(new TeamData(cycle, new List<PlayerID> { player }));
                store = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

                Assert.That(store.GetValue<TeamData>(slot).TeamNumber, Is.EqualTo(cycle),
                    $"cycle {cycle}: generation {slot.Generation} must replay into a fresh store");
            }
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

        [Test]
        public void Save_TypeMap_UsesStableIds_NotFullNames()
        {
            string json = GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());
            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(json)!;

            // The type map records stable IDs (#070), never raw C# type names — so renaming a type is safe.
            Assert.That(file.TypeMap.Any(t => t.TypeId == "model"), Is.True);
            Assert.That(file.TypeMap.Any(t => t.TypeId == "unit"), Is.True);
            Assert.That(file.TypeMap.Any(t => t.TypeId.Contains("FDG.")), Is.False,
                "no type-map entry should record a Type.FullName.");
        }

        [Test]
        public void PolymorphicPayloads_SerializeWithStableIds_AndRoundTrip()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            // A model on a RECTANGULAR base (IBaseShape $type) carrying a token that has both a payload and
            // a clear-trigger (TokenPayload + TokenClearTrigger $types — the latter is on every token).
            var model = new ModelData(new RectangleBase(1f, 2f), new List<Weapon>(), new Position(3, 4), store);
            model.Tokens.AddToken(new Token(TokenType.Shaken, 1,
                new TokenClearTrigger.RoundEnd(),
                new TokenPayload.RuleGrant("Stealth", ELifetime.UntilEndOfGame)));
            DataReference modelRef = store.Create(model);

            // Terrain whose shape is a composite of a rectangle + circle, and a second wrapped in a rotation
            // (CompositeZone / RectangularZone / CircularZone / RotatedZoneWrapper $types, incl. nesting).
            store.Create(new TerrainData(ETerrainType.Cover, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 2, 0, 2),
                new CircularZone(5, 5, 1),
            })));
            store.Create(new TerrainData(ETerrainType.Blocking,
                new RotatedZoneWrapper(new RectangularZone(0, 4, 0, 1), 45f, new Float2(2, 0.5f))));

            string json = GameSaveSerializer.Save(store);

            // No polymorphic $type may still carry an assembly-qualified engine type name — every persisted
            // polymorphic family must resolve through the stable-ID binder (#070). Catches an unregistered
            // payload family or the binder not being installed.
            Assert.That(json, Does.Not.Contain(", FutureOfDarkGrimness"),
                "a polymorphic $type serialized as an assembly-qualified name — a payload type is missing " +
                "from SaveTypeRegistry (or the binder isn't installed).");

            GameDataStore loaded = GameSaveSerializer.Load(json);

            // Concrete polymorphic types survive the round-trip through the binder.
            ModelData lModel = loaded.GetValue<ModelData>(modelRef);
            Assert.That(lModel.BaseShape, Is.TypeOf<RectangleBase>());

            Token lToken = lModel.Tokens.GetAllTokens().Single();
            Assert.That(lToken.ClearTrigger, Is.TypeOf<TokenClearTrigger.RoundEnd>());
            Assert.That(lToken.Payload, Is.TypeOf<TokenPayload.RuleGrant>());

            List<TerrainData> terrain = loaded.GetAllValues<TerrainData>().ToList();
            Assert.That(terrain.Select(t => t.Shape.GetType()),
                Is.EquivalentTo(new[] { typeof(CompositeZone), typeof(RotatedZoneWrapper) }));
        }

        [Test]
        public void LegacyFullNameTypeMap_StillLoads()
        {
            // Simulate a pre-#070 save whose type map recorded Type.FullName instead of stable IDs: the
            // FullName fallback in ResolveType must still rebuild the store.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(GameSaveSerializer.Save(store))!;
            foreach (SavedTypeEntry entry in file.TypeMap)
            {
                if (SaveTypeRegistry.TryGetType(entry.TypeId, out Type t))
                {
                    entry.TypeId = t.FullName!;
                }
            }
            string legacyish = JsonConvert.SerializeObject(file);

            Assert.DoesNotThrow(() => GameSaveSerializer.Load(legacyish));
        }

        // Reserve is unit state now (TokenType.InReserve), but saves written before that carry no token —
        // only the old "every model sits at the origin" tell. Load re-derives the token so a held-back
        // Ambush unit is still offered its arrival. See GameSaveSerializer.StampLegacyReserves.
        [Test]
        public void Load_LegacySaveWithUnplacedUnit_RederivesTheReserveToken()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());

            DataReference reserveRef = MakeUnitAt(store, player, "Infiltrators", new Position(0f, 0f));
            DataReference placedRef  = MakeUnitAt(store, player, "Warriors", new Position(10f, 10f));

            // Saved by an older build: no InReserve token anywhere.
            Assert.That(store.GetValue<UnitData>(reserveRef).Tokens.HasToken(TokenType.InReserve), Is.False);

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

            Assert.That(ReserveRules.IsInReserve(loaded.GetValue<UnitData>(reserveRef)), Is.True,
                "an unplaced unit is re-derived as a reserve, so it can still arrive.");
            Assert.That(ReserveRules.IsInReserve(loaded.GetValue<UnitData>(placedRef)), Is.False,
                "a deployed unit is not a reserve.");
            Assert.That(loaded.GetValue<UnitData>(placedRef).GetIsOnBattlefield(), Is.True);
        }

        private static DataReference MakeUnitAt(GameDataStore store, PlayerID player, string name, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(player, name, 4, 4, new List<DataBinding<ModelData>> { modelBinding });
            return store.Create(unit);
        }
    }
}
