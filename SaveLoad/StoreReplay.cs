using FDG.Data;
using FDG.Data.Containers;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Shared "rebuild a <see cref="GameDataStore"/> from serialized entries" pipeline used by BOTH the
    /// save/load resume path (<see cref="GameSaveSerializer"/>) and the full-state network sync
    /// (<c>GameDataUpdateReceiver.OnReceivedAllDataMessage</c>).
    /// <para>
    /// The snapshot lists entries in raw store (registration) order, which is NOT dependency order: a
    /// <see cref="DataBinding{T}"/> field can point into a store that hasn't been replayed yet -
    /// e.g. every <c>ModelData</c> carries a <c>DataBinding&lt;Float2&gt;</c> facing but <c>Float2</c> is
    /// registered last (#150), and an <c>ObjectiveData</c> holds a <c>DataBinding&lt;PlayerID&gt;</c> owner.
    /// A retry-less single pass therefore throws <c>IsNotAssigned</c> on the first forward reference and
    /// abandons the rest of the graph. <see cref="Rebuild"/> replays with deferred retry so forward
    /// references resolve on a later pass, then rehydrates the <c>[JsonIgnore]</c> rule blobs (#095) and
    /// rewires the wound subscriptions the JSON constructors intentionally skip.
    /// </para>
    /// </summary>
    public static class StoreReplay
    {
        /// <summary>
        /// Replay every entry (with deferred retry), then rehydrate rules and rewire subscriptions.
        /// Leaves <paramref name="store"/> as a faithful copy of the source store the entries came from.
        /// </summary>
        public static void Rebuild(IReadWriteableGameDataStore store, IReadOnlyList<ReferenceJsonValuePair> entries)
        {
            ReplayEntriesWithRetry(store, entries);
            RewireSubscriptions(store);
            RehydrateRuleDefinitions(store);
        }

        // Replay every entry, deferring any whose DataBinding fields can't yet resolve and retrying
        // them on the next pass. A failed CreateFromReferenceAndJson writes nothing (it throws while
        // deserializing, before the store is touched), so deferred entries are safe to retry.
        public static void ReplayEntriesWithRetry(IReadWriteableGameDataStore store, IReadOnlyList<ReferenceJsonValuePair> entries)
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
                        // #270: the snapshot path, which adopts each entry's generation - a slot recycled
                        // during the session (destroyed, then refilled) is past generation 1, and this
                        // store starts every slot at 0.
                        store.CreateFromReplayJson(pair.DataReference, pair.JsonValue);
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
                        $"Store replay stalled with {stillPending.Count} unresolved entr(ies); a referenced " +
                        "value is missing or the references are cyclic.", lastError);
                }

                pending = stillPending;
            }
        }

        public static void RewireSubscriptions(IReadWriteableGameDataStore store)
        {
            if (store.IsTypeAssigned<UnitData>())
            {
                foreach (UnitData unit in store.GetAllValues<UnitData>())
                {
                    unit.RewireModelWoundSubscriptions();
                }
            }
        }

        // #095: RuleDefinitions is [JsonIgnore] on every carrier; each persists its attached rules as an
        // STJ blob (RuleAttachmentPersistence) which we replay back onto the live lists here, so a resumed
        // (or network-synced) game carries the same unit/model/weapon rules the host has. Restores rule
        // OBJECTS only - creation-time effects (e.g. Tough's max-wounds) are NOT re-applied; those stay
        // gated to fresh-game creation in FDGServer, so this neither loses the rule nor doubles its effect.
        public static void RehydrateRuleDefinitions(IReadWriteableGameDataStore store)
        {
            if (store.IsTypeAssigned<UnitData>())
            {
                foreach (UnitData unit in store.GetAllValues<UnitData>())
                {
                    unit.RehydrateRules();
                }
            }

            if (store.IsTypeAssigned<ModelData>())
            {
                foreach (ModelData model in store.GetAllValues<ModelData>())
                {
                    model.RehydrateRules();
                    foreach (Weapon weapon in model.Weapons)
                    {
                        weapon.RehydrateRules();
                    }
                }
            }
        }
    }
}
