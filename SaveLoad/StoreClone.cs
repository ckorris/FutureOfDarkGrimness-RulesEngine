using FDG.Data;
using FDG.Data.Containers;
using FDG.Players;

namespace FDG.SaveLoad
{
    /// <summary>
    /// #394: a typed, in-memory copy of a whole <see cref="GameDataStore"/> - the store that
    /// <c>GameSaveSerializer.Load(GameSaveSerializer.Save(source))</c> would produce, built without
    /// the text. The search snapshots the game at every tree node and resumes a copy for every
    /// simulation, and a CPU profile of a Strategist game put 41% of its cycles in that JSON round trip
    /// (plus a finalizer-thread tail from the reflection stubs the loader emitted per store).
    /// <para>
    /// <b>The contract is equivalence with the serializer, not a faithful copy of the live object.</b>
    /// Every decision below follows from "what does Load(Save(x)) give?": [JsonIgnore] state comes
    /// back as after a load (empty spells - the resume path restores them from the persisted blob on
    /// both paths; live rule lists - the blob would rehydrate to equal values, so they are shared
    /// instead of re-parsed), no event subscriber survives (the JSON constructors wire none; the wound
    /// aggregation is rewired afterwards exactly as <see cref="StoreReplay.Rebuild"/> does), free slots
    /// read as never used (a save records only occupied ones), and the legacy-reserve stamp runs.
    /// Immutable values (shapes, zones, resolved rules, tokens) are shared; anything play mutates
    /// (token containers, weapons, the hero attachment, every list) is copied. The pin is
    /// <c>Save(Clone(x)) == Save(x)</c> on played boards, and a search run through both paths choosing
    /// identically (StoreCloneTests).
    /// </para>
    /// <para>
    /// A registered type without a typed cloner here (a test-only type, or a component added later)
    /// falls back to the serializer for its entries, after the typed ones, through the same
    /// checked replay the loader uses - slower, never wrong. Adding a cloner for a new engine type is
    /// the fast path; the equivalence test catches a cloner that drops a field.
    /// </para>
    /// </summary>
    public static class StoreClone
    {
        public static GameDataStore Clone(GameDataStore source)
        {
            GameDataStore target = GameDataStore.CreateFromTypeMap(source.GetTypeMapWithCapacities());

            List<ReferenceJsonValuePair>? fallback = null;
            IReadOnlyList<Type> types = source.RegisteredTypes;
            for (int i = 1; i < types.Count; i++)
            {
                if (s_cloners.TryGetValue(types[i], out Action<GameDataStore, GameDataStore>? cloner))
                {
                    cloner(source, target);
                }
                else
                {
                    fallback ??= new List<ReferenceJsonValuePair>();
                    IComponentStore store = source.ComponentStoreOf(new TypeID(i));
                    foreach (DataReference reference in store.GetAllDataReferences())
                    {
                        fallback.Add(new ReferenceJsonValuePair(reference,
                            source.SerializeValue(store.GetValueUntyped(reference)!)));
                    }
                }
            }

            if (fallback != null)
            {
                StoreReplay.ReplayEntriesWithRetry(target, fallback);
            }

            // The loader's post-replay steps, in its order (StoreReplay.Rebuild, then Load's stamp).
            StoreReplay.RewireSubscriptions(target);
            StoreReplay.RehydrateRuleDefinitions(target);
            GameSaveSerializer.StampLegacyReserves(target);
            return target;
        }

        // One entry per engine component type (GameDataStoreBuilder.GetDefault), keyed by the type.
        private static readonly Dictionary<Type, Action<GameDataStore, GameDataStore>> s_cloners = new()
        {
            // Value types and immutables: the value itself.
            [typeof(int)] = Copy<int>(static (v, _) => v),
            [typeof(float)] = Copy<float>(static (v, _) => v),
            [typeof(string)] = Copy<string>(static (v, _) => v),
            [typeof(Position)] = Copy<Position>(static (v, _) => v),
            [typeof(Float2)] = Copy<Float2>(static (v, _) => v),
            [typeof(PlayerID)] = Copy<PlayerID>(static (v, _) => v),
            [typeof(PlayerSlotInfo)] = Copy<PlayerSlotInfo>(static (v, _) => v),
            // Immutable reference types (readonly fields / get-only properties, immutable zones inside).
            [typeof(RectangularZone)] = Copy<RectangularZone>(static (v, _) => v),
            [typeof(TerrainData)] = Copy<TerrainData>(static (v, _) => v),

            [typeof(TeamData)] = Copy<TeamData>(static (team, _) =>
                new TeamData(team.TeamNumber, CopyList(team.Players)!)),

            [typeof(ObjectiveData)] = Copy<ObjectiveData>(static (objective, target) =>
                new ObjectiveData(objective.Position, Rebind(target, objective.OwnerIDBinding)!)),

            [typeof(ModelData)] = Copy<ModelData>(static (model, target) =>
                new ModelData(model,
                    Rebind(target, model.RemainingWoundsBinding)!,
                    Rebind(target, model.PositionBinding)!,
                    Rebind(target, model.FacingBinding)!)),

            [typeof(UnitData)] = Copy<UnitData>(static (unit, target) =>
                new UnitData(unit, RebindList(target, unit.ModelBindings)!)),

            [typeof(ArmyData)] = Copy<ArmyData>(static (army, target) =>
                new ArmyData(army, RebindList(target, army.UnitBindings)!)),

            [typeof(GameProgressData)] = Copy<GameProgressData>(static (progress, target) =>
                new GameProgressData(
                    progress.Stage,
                    progress.RoundCount,
                    CopyList(progress.TeamActivateOrder)!,
                    CopyList(progress.CurrentRoundTeamFinishOrder)!,
                    progress.CurrentTeamIndex,
                    progress.CurrentPlayerIndexPerTeam == null ? null! : new Dictionary<int, int>(progress.CurrentPlayerIndexPerTeam),
                    RebindList(target, progress.UnactivatedUnits)!,
                    progress.Settings,
                    Rebind(target, progress.ActivatingUnit))),
        };

        private static Action<GameDataStore, GameDataStore> Copy<T>(Func<T, GameDataStore, T> cloneValue) =>
            (source, target) => target.ComponentStoreOf<T>().ReplayFrom(source.ComponentStoreOf<T>(),
                value => cloneValue(value, target));

        /// <summary>The target store's binding for the same reference - the shared, per-slot one.</summary>
        private static DataBinding<T>? Rebind<T>(GameDataStore target, DataBinding<T>? binding) =>
            binding == null ? null : target.BindForReplay<T>(binding.Reference);

        private static List<DataBinding<T>>? RebindList<T>(GameDataStore target, List<DataBinding<T>>? bindings)
        {
            if (bindings == null) return null;
            var rebound = new List<DataBinding<T>>(bindings.Count);
            foreach (DataBinding<T> binding in bindings)
            {
                rebound.Add(Rebind(target, binding)!);
            }
            return rebound;
        }

        private static List<T>? CopyList<T>(IReadOnlyList<T>? list) => list == null ? null : new List<T>(list);
    }
}
