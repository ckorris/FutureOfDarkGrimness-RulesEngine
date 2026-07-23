using FDG.Data;
using FDG.Data.Containers;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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
        // v2 (#070): the type map records stable IDs (SaveTypeRegistry) rather than raw Type.FullName, and
        // polymorphic $type payloads inside entries do too (via AllowlistSerializationBinder). Bumped from
        // v1; nothing is distributed, so there are no v1 saves in the wild to migrate — the FullName
        // fallback in ResolveType/the binder is cheap insurance rather than a live compatibility path.
        public const int CurrentVersion = 2;

        public static string Save(GameDataStore store)
        {
            GameSaveFile file = new GameSaveFile
            {
                Version = CurrentVersion,
                TypeMap = store.GetTypeMapWithCapacities()
                    .Select(tc => new SavedTypeEntry(SaveTypeRegistry.GetIdOrFullName(tc.Type), tc.Capacity))
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
                .Select(entry => new GameDataStore.TypeAndCapacity(ResolveType(entry.TypeId), entry.Capacity))
                .ToList();

            GameDataStore store = GameDataStore.CreateFromTypeMap(typeMap);

            // Replay entries (with forward-reference retry), rehydrate [JsonIgnore] rule blobs, and rewire
            // the wound subscriptions the JSON constructors skip. Shared with the full-state network sync
            // (GameDataUpdateReceiver) so both rebuild paths behave identically. See StoreReplay.
            StoreReplay.Rebuild(store, file.Entries);

            StampLegacyReserves(store);

            return store;
        }

        /// <summary>
        /// Reserve used to be inferred from "every model of this unit sits at the world origin"; it is now
        /// unit state (<see cref="TokenType.InReserve"/>). A save written before that change carries no such
        /// token, so its held-back Ambush units would never be offered to arrive. Re-derive the token once,
        /// on load, from the old positional rule.
        ///
        /// Save-path only, deliberately: the network full-state sync shares StoreReplay but must stay a
        /// faithful mirror of the host's store, and the host already sends the token.
        ///
        /// A unit still awaiting its first deployment also has every model at the origin and gets stamped
        /// here. That is harmless - deployment clears the token the moment it places the unit - and it is
        /// what keeps this from needing a save-version bump (see #178).
        /// </summary>
        private static void StampLegacyReserves(IReadWriteableGameDataStore store)
        {
            if (!store.IsTypeAssigned<UnitData>()) return;

            foreach (UnitData unit in store.GetAllValues<UnitData>())
            {
                if (!unit.GetIsAlive()) continue;
                if (ReserveRules.IsInReserve(unit)) continue;                              // saved post-change
                if (unit.Tokens.HasToken(TokenType.EmbarkedIn)) continue;                  // aboard a transport
                if (unit.Tokens.HasToken(TokenType.OffTableFromForcedMove)) continue;      // aircraft, flew off

                bool everyLivingModelAtOrigin = true;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (model.Position.x != 0f || model.Position.z != 0f)
                    {
                        everyLivingModelAtOrigin = false;
                        break;
                    }
                }

                if (everyLivingModelAtOrigin) ReserveRules.PlaceInReserve(unit);
            }
        }

        // Resolve a saved type-map entry's identity: a stable ID first (#070), then a Type.FullName fallback
        // for anything unregistered (an unmapped type, or a hypothetical pre-#070 save that stored FullNames).
        private static Type ResolveType(string typeId)
        {
            if (SaveTypeRegistry.TryGetType(typeId, out Type mapped))
            {
                return mapped;
            }

            // Fallback: a legacy FullName for an unregistered type (SaveTypeRegistry writes plain,
            // never assembly-qualified, FullNames). This is the save's top-level type map, which is
            // attacker-controlled on an untrusted save, so:
            //  - resolve within the engine assembly, or as an UNQUALIFIED core name (primitives like
            //    System.Int32) - the no-comma guard means we never load an arbitrary assembly the way
            //    a qualified Type.GetType would, and
            //  - gate the result through the same allowlist the entry binder uses (#265), so even a
            //    resolvable core type that isn't an allowed leaf/engine type is refused.
            Type? type = typeof(GameDataStore).Assembly.GetType(typeId);
            if (type == null && !typeId.Contains(','))
            {
                type = Type.GetType(typeId);
            }

            if (type == null || !AllowlistSerializationBinder.IsAllowed(type))
            {
                throw new InvalidOperationException(
                    $"Save references type '{typeId}', which is unregistered or outside the load allowlist (#265).");
            }

            return type;
        }
    }
}
