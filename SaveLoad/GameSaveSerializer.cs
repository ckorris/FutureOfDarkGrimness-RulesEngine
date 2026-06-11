using FDG.Data;
using FDG.Data.Containers;
using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Serializes a whole <see cref="GameDataStore"/> to a save string and rebuilds it on load.
    /// <para>
    /// Save = type-map fingerprint (with capacities) + every store entry as reference/JSON. Load =
    /// resolve the fingerprint back to a store via <see cref="GameDataStore.CreateFromTypeMap(List{GameDataStore.TypeAndCapacity})"/>,
    /// replay the entries (with retry, since a <see cref="DataBinding{T}"/> field may reference an
    /// entry that hasn't been recreated yet — e.g. an objective's owner <see cref="PlayerID"/>),
    /// then re-wire post-load event subscriptions the JSON constructors intentionally skip.
    /// </para>
    /// </summary>
    public static class GameSaveSerializer
    {
        public const int CurrentVersion = 1;

        public static string Save(GameDataStore store)
        {
            GameSaveFile file = new GameSaveFile
            {
                Version = CurrentVersion,
                TypeMap = store.GetTypeMapWithCapacities()
                    .Select(tc => new SavedTypeEntry(tc.Type.FullName!, tc.Capacity))
                    .ToList(),
                Entries = store.GetAllDataReferencesAsJson(),
            };

            return JsonConvert.SerializeObject(file, Formatting.Indented);
        }

        public static GameDataStore Load(string json)
        {
            GameSaveFile? file = JsonConvert.DeserializeObject<GameSaveFile>(json);
            if (file == null)
            {
                throw new InvalidOperationException("Save data could not be parsed.");
            }

            if (file.Version != CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported save version {file.Version} (this build expects {CurrentVersion}).");
            }

            List<GameDataStore.TypeAndCapacity> typeMap = file.TypeMap
                .Select(entry => new GameDataStore.TypeAndCapacity(ResolveType(entry.FullName), entry.Capacity))
                .ToList();

            GameDataStore store = GameDataStore.CreateFromTypeMap(typeMap);

            ReplayEntries(store, file.Entries);
            RewireSubscriptions(store);

            return store;
        }

        // Replay every entry, deferring any whose DataBinding fields can't yet resolve and retrying
        // them on the next pass. A failed CreateFromReferenceAndJson writes nothing (it throws while
        // deserializing, before the store is touched), so deferred entries are safe to retry.
        private static void ReplayEntries(GameDataStore store, List<ReferenceJsonValuePair> entries)
        {
            List<ReferenceJsonValuePair> pending = new List<ReferenceJsonValuePair>(entries);

            while (pending.Count > 0)
            {
                List<ReferenceJsonValuePair> stillPending = new List<ReferenceJsonValuePair>();
                Exception? lastError = null;

                foreach (ReferenceJsonValuePair pair in pending)
                {
                    try
                    {
                        store.CreateFromReferenceAndJson(pair.DataReference, pair.JsonValue);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        stillPending.Add(pair);
                    }
                }

                if (stillPending.Count == pending.Count)
                {
                    throw new InvalidOperationException(
                        $"Save load stalled with {stillPending.Count} unresolved entr(ies); a referenced " +
                        "value is missing or the references are cyclic.", lastError);
                }

                pending = stillPending;
            }
        }

        private static void RewireSubscriptions(GameDataStore store)
        {
            if (store.IsTypeAssigned<UnitData>())
            {
                foreach (UnitData unit in store.GetAllValues<UnitData>())
                {
                    unit.RewireModelWoundSubscriptions();
                }
            }
        }

        private static Type ResolveType(string fullName)
        {
            // Registered types live either in the engine assembly or in the runtime (primitives).
            Type? type = typeof(GameDataStore).Assembly.GetType(fullName) ?? Type.GetType(fullName);
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"Save references type '{fullName}', which this build can't resolve.");
            }

            return type;
        }
    }
}
