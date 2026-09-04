using System.Text.RegularExpressions;
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

        // A serialized DataReference, as Newtonsoft writes the struct's three public fields in
        // declaration order (DataBinding fields serialize as exactly their reference - see
        // DataBindingJsonConverter.WriteJson). Only a hint: a shape this scan misses falls back to the
        // exception path below, so a future change to the serialized form costs speed, not correctness.
        private static readonly Regex ReferencePattern = new Regex(
            @"""TypeID"":\{""ID"":(-?\d+)\},""Index"":(-?\d+),""Generation"":(-?\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Replay every entry, deferring any whose DataBinding fields can't yet resolve and retrying
        // them on the next pass. A failed CreateFromReferenceAndJson writes nothing (it throws while
        // deserializing, before the store is touched), so deferred entries are safe to retry.
        //
        // Readiness is CHECKED, not discovered by catching (#191 R9): an entry is replayed only once
        // every reference embedded in its JSON resolves in the store, so the ordinary forward
        // reference (a model's facing binding into the Float2 store registered after it) costs a
        // lookup rather than a thrown-and-swallowed exception. The old loader threw ~200 first-chance
        // exceptions per 2k snapshot load; a search loads a snapshot per simulation, and under an
        // attached debugger every first-chance exception is a stop-the-process event - which is what
        // froze the GUI at the Strategist's first activation. The exception path is kept as the
        // fallback for what the check cannot see: a dangling reference (a plain DataReference to a
        // destroyed value is legal and deserializes fine) or a genuine cycle, both of which are only
        // attempted once nothing else can make progress, so they still get the old behaviour.
        public static void ReplayEntriesWithRetry(IReadWriteableGameDataStore store, IReadOnlyList<ReferenceJsonValuePair> entries)
        {
            var pending = new List<(ReferenceJsonValuePair Pair, DataReference[] References)>(entries.Count);
            foreach (ReferenceJsonValuePair pair in entries)
            {
                pending.Add((pair, ScanReferences(pair.JsonValue)));
            }

            while (pending.Count > 0)
            {
                bool anyReady = false;
                foreach ((ReferenceJsonValuePair _, DataReference[] references) in pending)
                {
                    if (AllResolve(store, references))
                    {
                        anyReady = true;
                        break;
                    }
                }

                var stillPending = new List<(ReferenceJsonValuePair Pair, DataReference[] References)>();
                Exception? lastError = null;

                foreach ((ReferenceJsonValuePair pair, DataReference[] references) in pending)
                {
                    // While anything is ready, only ready entries are attempted; the blind attempt (the
                    // exception path) is reserved for the pass where nothing resolves by inspection.
                    if (anyReady && AllResolve(store, references) == false)
                    {
                        stillPending.Add((pair, references));
                        continue;
                    }

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
                        stillPending.Add((pair, references));
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

        /// <summary>Every DataReference embedded in one entry's JSON (bindings and plain references alike).</summary>
        internal static DataReference[] ScanReferences(string json)
        {
            MatchCollection matches = ReferencePattern.Matches(json);
            if (matches.Count == 0) return Array.Empty<DataReference>();

            var references = new DataReference[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                references[i] = new DataReference
                {
                    TypeID = new TypeID(int.Parse(matches[i].Groups[1].Value)),
                    Index = int.Parse(matches[i].Groups[2].Value),
                    Generation = int.Parse(matches[i].Groups[3].Value),
                };
            }
            return references;
        }

        private static bool AllResolve(IReadableGameDataStore store, DataReference[] references)
        {
            foreach (DataReference reference in references)
            {
                if (store.IsValid(reference, out _) == false) return false;
            }
            return true;
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
