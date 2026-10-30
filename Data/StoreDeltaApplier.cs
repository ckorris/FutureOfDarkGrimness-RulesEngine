using FDG.Replay;

namespace FDG.Data
{
    /// <summary>
    /// How a mirror store treats the generation an incoming add carries (#454).
    /// </summary>
    public enum EDeltaGenerationPolicy
    {
        /// <summary>
        /// The live stream (a networked client): the mirror is in step with the sender since the join, so an
        /// add's generation must be exactly the next one for its slot - anything else means a message was
        /// missed or arrived out of order, and the add throws.
        /// </summary>
        Sequential,

        /// <summary>
        /// Replay playback: the mirror was rebuilt from a snapshot, which carries no generation for an empty
        /// slot, so an add into a slot the game had recycled before the snapshot is legitimately "ahead" of
        /// the rebuilt store. The add adopts the recorded generation, exactly as a snapshot rebuild does
        /// (#270). The other foreign-create checks (index, type, slot not already filled) still apply.
        /// </summary>
        Adopt,
    }

    /// <summary>
    /// Applies one recorded or replicated store delta - an add, an update or a remove - to a mirror store.
    /// The one implementation shared by the networked client's incremental sync
    /// (<c>GameDataUpdateReceiver</c>) and replay playback (<c>ReplayPlayer</c>, #454), so a replay is
    /// played back by exactly the code a client uses to follow a live game.
    /// <para>
    /// Whole-store rebuilds (a save, the join-time catch-up, a replay snapshot) are not deltas; they go
    /// through <c>StoreReplay.Rebuild</c>.
    /// </para>
    /// </summary>
    public static class StoreDeltaApplier
    {
        public static void ApplyAdd(IReadWriteableGameDataStore store, DataReference reference, string json,
            EDeltaGenerationPolicy policy = EDeltaGenerationPolicy.Sequential)
        {
            if (policy == EDeltaGenerationPolicy.Adopt)
            {
                store.CreateFromReplayJson(reference, json);
            }
            else
            {
                store.CreateFromReferenceAndJson(reference, json);
            }
        }

        /// <summary>Replaces the entry with a freshly deserialized value (the sender always sends the whole
        /// value). Throws if <paramref name="reference"/> is not live in the mirror.</summary>
        public static void ApplyUpdate(IReadWriteableGameDataStore store, DataReference reference, string json) =>
            store.SetValueWithJson(reference, json);

        /// <summary>Destroys the entry. Returns false, without throwing, if it was already gone.</summary>
        public static bool ApplyRemove(IReadWriteableGameDataStore store, DataReference reference) =>
            store.Destroy(reference);

        /// <summary>
        /// Applies <paramref name="replayEvent"/> if it is a store delta (<see cref="StoreAddEvent"/>,
        /// <see cref="StoreUpdateEvent"/> or <see cref="StoreRemoveEvent"/>) and returns true; any other event is
        /// left alone and returns false.
        /// </summary>
        public static bool Apply(IReadWriteableGameDataStore store, ReplayEvent replayEvent,
            EDeltaGenerationPolicy policy = EDeltaGenerationPolicy.Sequential)
        {
            switch (replayEvent)
            {
                case StoreAddEvent add:
                    ApplyAdd(store, add.Ref, add.Json, policy);
                    return true;
                case StoreUpdateEvent update:
                    ApplyUpdate(store, update.Ref, update.Json);
                    return true;
                case StoreRemoveEvent remove:
                    ApplyRemove(store, remove.Ref);
                    return true;
                default:
                    return false;
            }
        }
    }
}
