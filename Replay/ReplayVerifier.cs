using FDG.Data;
using FDG.Data.Containers;
using FDG.Network;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StageResolution;
using Newtonsoft.Json;

namespace FDG.Replay
{
    /// <summary>One thing <see cref="ReplayVerifier"/> reported, anchored to the event it is about.</summary>
    public sealed class ReplayVerifyIssue
    {
        /// <summary>The event the issue is about: the first missing Seq of a gap, the request, the delta that
        /// would not apply, or - for a snapshot divergence - the earliest last-change among the entries that
        /// differ (see <see cref="ReplayVerifier"/>).</summary>
        public long Seq { get; }

        /// <summary>Which check: <c>seq</c>, <c>request</c>, <c>marker</c>, <c>snapshot</c>, <c>delta</c> or <c>beat</c>.</summary>
        public string Check { get; }

        /// <summary>One ASCII line.</summary>
        public string Message { get; }

        /// <summary>Further ASCII lines (a divergence's entries, one per line).</summary>
        public IReadOnlyList<string> Details { get; }

        internal ReplayVerifyIssue(long seq, string check, string message, IReadOnlyList<string>? details = null)
        {
            Seq = seq;
            Check = check;
            Message = ReplayText.Ascii(message);
            Details = (details ?? Array.Empty<string>()).Select(ReplayText.Ascii).ToList();
        }
    }

    /// <summary>What <see cref="ReplayVerifier.Verify"/> found. <see cref="Passed"/> means no failures; notes are
    /// facts worth knowing that do not make the recording wrong (an abandoned game, a failed request).</summary>
    public sealed class ReplayVerification
    {
        public IReadOnlyList<ReplayVerifyIssue> Failures { get; }

        public IReadOnlyList<ReplayVerifyIssue> Notes { get; }

        /// <summary>Snapshots after the first that were rebuilt from their predecessor's deltas and compared.</summary>
        public int SnapshotsCompared { get; }

        /// <summary>Store deltas applied across the whole recording.</summary>
        public int DeltasApplied { get; }

        /// <summary>Beats read back against the store at their position.</summary>
        public int BeatsRead { get; }

        public bool Passed => Failures.Count == 0;

        internal ReplayVerification(IReadOnlyList<ReplayVerifyIssue> failures, IReadOnlyList<ReplayVerifyIssue> notes,
            int snapshotsCompared, int deltasApplied, int beatsRead)
        {
            Failures = failures;
            Notes = notes;
            SnapshotsCompared = snapshotsCompared;
            DeltasApplied = deltasApplied;
            BeatsRead = beatsRead;
        }
    }

    /// <summary>
    /// Checks a recording's integrity (#455), without a front end:
    /// <list type="bullet">
    /// <item><b>seq</b> - <c>Seq</c> starts at 0 and is dense (a gap is a lost event).</item>
    /// <item><b>request</b> - every request was answered or failed, unless it was still open when the recording
    /// stopped (no later request from the same player was answered); no reply without its request.</item>
    /// <item><b>marker</b> - at most one <c>GameEnded</c>; none at all is a note (abandoned or cut short).</item>
    /// <item><b>snapshot</b> - each snapshot after the first, unless a <c>Resumed</c> marker separates it from the
    /// one before, equals (as a re-saved store) the state its predecessor plus the deltas between them build. A
    /// divergence lists the entries that differ, each with its differing top-level properties and the Seq of its
    /// last recorded change; the issue is anchored to the earliest of those. The check then carries on from the
    /// recorded snapshot, so one bad delta is reported once.</item>
    /// <item><b>delta</b> / <b>beat</b> - every store delta applies and every beat reads back at its position.</item>
    /// </list>
    /// Snapshots load exactly as playback loads them (no legacy reserve stamp) and adds adopt their recorded
    /// generation, so a clean recording verifies clean (plan 4.3).
    /// </summary>
    public static class ReplayVerifier
    {
        public static ReplayVerification Verify(ReplayTimeline timeline)
        {
            var failures = new List<ReplayVerifyIssue>();
            var notes = new List<ReplayVerifyIssue>();

            CheckSeq(timeline, failures);
            CheckRequests(timeline, failures, notes);
            CheckMarkers(timeline, failures, notes);
            var counts = CheckSnapshots(timeline, failures, notes);

            return new ReplayVerification(failures, notes, counts.Compared, counts.Deltas, counts.Beats);
        }

        // --- seq ---------------------------------------------------------------------------------------

        private static void CheckSeq(ReplayTimeline timeline, List<ReplayVerifyIssue> failures)
        {
            IReadOnlyList<ReplayEvent> events = timeline.Events;
            if (events.Count == 0)
            {
                return;
            }

            if (events[0].Seq != 0)
            {
                failures.Add(new ReplayVerifyIssue(0, "seq", $"The recording starts at Seq {events[0].Seq}, not 0: {Count(events[0].Seq, "event")} missing."));
            }

            for (int i = 1; i < events.Count; i++)
            {
                long expected = events[i - 1].Seq + 1;
                if (events[i].Seq != expected)
                {
                    long missing = events[i].Seq - expected;
                    string which = missing == 1 ? $"Event {expected} is missing" : $"Events {expected}-{events[i].Seq - 1} are missing";
                    failures.Add(new ReplayVerifyIssue(expected, "seq", $"{which} (Seq {events[i - 1].Seq} is followed by {events[i].Seq})."));
                }
            }
        }

        // --- request -----------------------------------------------------------------------------------

        private static void CheckRequests(ReplayTimeline timeline, List<ReplayVerifyIssue> failures, List<ReplayVerifyIssue> notes)
        {
            // A player's request is still open when the recording stopped if that player answered nothing later.
            var lastAnsweredByPlayer = new Dictionary<PlayerID, long>();
            foreach (ReplayRequest request in timeline.Requests)
            {
                if (request.IsAnswered || request.Error != null)
                {
                    lastAnsweredByPlayer[request.Request.PlayerId] = request.Request.Seq;
                }
            }

            int open = 0;
            foreach (ReplayRequest request in timeline.Requests)
            {
                RequestEvent asked = request.Request;
                if (request.Error != null)
                {
                    notes.Add(new ReplayVerifyIssue(request.Error.Seq, "request",
                        $"{ShortType(asked.RequestType)} (Seq {asked.Seq}) failed: {request.Error.Message}"));
                }
                else if (request.IsAnswered == false)
                {
                    if (lastAnsweredByPlayer.TryGetValue(asked.PlayerId, out long later) && later > asked.Seq)
                    {
                        failures.Add(new ReplayVerifyIssue(asked.Seq, "request",
                            $"{ShortType(asked.RequestType)} (Seq {asked.Seq}, task {Short(asked.TaskId)}) was never answered, " +
                            $"but the same player answered a later request (Seq {later})."));
                    }
                    else
                    {
                        open++;
                    }
                }
            }

            if (open > 0)
            {
                notes.Add(new ReplayVerifyIssue(timeline.Events[^1].Seq, "request",
                    $"{Count(open, "request")} still open when the recording stopped."));
            }

            // Replies and errors for tasks the recording never saw requested, and second answers to one task. The
            // timeline ignores both; for a host recording they mean an event went missing or was recorded twice.
            var answered = new HashSet<TaskID>();
            foreach (ReplayEvent replayEvent in timeline.Events)
            {
                (TaskID task, string kind)? answer = replayEvent switch
                {
                    ReplyEvent reply => (reply.TaskId, "reply"),
                    RequestErrorEvent error => (error.TaskId, "error"),
                    _ => null,
                };
                if (answer == null)
                {
                    continue;
                }

                if (timeline.FindRequest(answer.Value.task) == null)
                {
                    var issue = new ReplayVerifyIssue(replayEvent.Seq, "request",
                        $"A {answer.Value.kind} at Seq {replayEvent.Seq} answers task {Short(answer.Value.task)}, which was never requested.");
                    (timeline.Header.RecordedBy == EReplayRecordedBy.Host ? failures : notes).Add(issue);
                }
                else if (answered.Add(answer.Value.task) == false)
                {
                    failures.Add(new ReplayVerifyIssue(replayEvent.Seq, "request",
                        $"Task {Short(answer.Value.task)} is answered a second time at Seq {replayEvent.Seq}."));
                }
            }
        }

        // --- marker ------------------------------------------------------------------------------------

        private static void CheckMarkers(ReplayTimeline timeline, List<ReplayVerifyIssue> failures, List<ReplayVerifyIssue> notes)
        {
            List<MarkerEvent> ended = timeline.Markers.Where(m => m.Marker == EReplayMarker.GameEnded).ToList();
            if (ended.Count == 0)
            {
                long last = timeline.Events.Count > 0 ? timeline.Events[^1].Seq : 0;
                notes.Add(new ReplayVerifyIssue(last, "marker", "No GameEnded marker: the game was abandoned or the recording was cut short."));
            }

            foreach (MarkerEvent extra in ended.Skip(1))
            {
                failures.Add(new ReplayVerifyIssue(extra.Seq, "marker",
                    $"A second GameEnded marker at Seq {extra.Seq} (the first is at Seq {ended[0].Seq})."));
            }
        }

        // --- snapshot / delta / beat -------------------------------------------------------------------

        private static (int Compared, int Deltas, int Beats) CheckSnapshots(ReplayTimeline timeline,
            List<ReplayVerifyIssue> failures, List<ReplayVerifyIssue> notes)
        {
            IReadOnlyList<SnapshotEvent> snapshots = timeline.Snapshots;
            if (snapshots.Count == 0)
            {
                failures.Add(new ReplayVerifyIssue(0, "snapshot", "The recording has no snapshot, so nothing in it can be played back."));
                return (0, 0, 0);
            }

            int compared = 0, deltas = 0, beats = 0;
            GameDataStore? store = null;           // the state built so far; null after a failure until the next load
            JsonSerializerSettings? beatSettings = null;
            var lastChange = new Dictionary<(int Type, int Index), long>();

            for (int i = 0; i < snapshots.Count; i++)
            {
                SnapshotEvent snapshot = snapshots[i];
                bool carriesOn = store != null;
                if (carriesOn && ResumedBetween(timeline, snapshots[i - 1].Seq, snapshot.Seq))
                {
                    notes.Add(new ReplayVerifyIssue(snapshot.Seq, "snapshot",
                        $"Snapshot {snapshot.Index} (Seq {snapshot.Seq}) follows a resume: it replaces the state, so it is not compared."));
                    carriesOn = false;
                }

                bool matches = false;
                if (carriesOn)
                {
                    compared++;
                    string built = GameSaveSerializer.Save(store!);
                    matches = built == snapshot.SaveJson;
                    if (matches == false)
                    {
                        failures.Add(Divergence(snapshot, snapshots[i - 1], built, store!, lastChange));
                    }
                }

                if (matches == false)
                {
                    try
                    {
                        store = GameSaveSerializer.Load(snapshot.SaveJson, stampLegacyReserves: false);
                        beatSettings = null;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new ReplayVerifyIssue(snapshot.Seq, "snapshot",
                            $"Snapshot {snapshot.Index} (Seq {snapshot.Seq}) does not load: {exception.Message}"));
                        store = null;
                    }
                }
                lastChange.Clear();

                // The deltas up to the next snapshot (or the end), stopping at a resume: what follows one is replaced
                // by the snapshot after it.
                long until = i + 1 < snapshots.Count ? snapshots[i + 1].Seq : long.MaxValue;
                bool resumed = false;
                for (int e = timeline.IndexOfSeq(snapshot.Seq + 1); e < timeline.Events.Count && timeline.Events[e].Seq < until; e++)
                {
                    ReplayEvent replayEvent = timeline.Events[e];
                    if (replayEvent is MarkerEvent { Marker: EReplayMarker.Resumed })
                    {
                        resumed = true;
                        continue;
                    }

                    if (replayEvent is StoreAddEvent or StoreUpdateEvent or StoreRemoveEvent)
                    {
                        if (resumed)
                        {
                            failures.Add(new ReplayVerifyIssue(replayEvent.Seq, "delta",
                                $"{replayEvent.Kind} at Seq {replayEvent.Seq} lies between a resume and its snapshot; playback cannot place it."));
                            continue;
                        }
                        if (store == null)
                        {
                            continue;
                        }

                        try
                        {
                            StoreDeltaApplier.Apply(store, replayEvent, EDeltaGenerationPolicy.Adopt);
                            deltas++;
                            DataReference reference = RefOf(replayEvent);
                            lastChange[(reference.TypeID.ID, reference.Index)] = replayEvent.Seq;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(new ReplayVerifyIssue(replayEvent.Seq, "delta",
                                $"{replayEvent.Kind} {Describe(store, RefOf(replayEvent))} at Seq {replayEvent.Seq} does not apply: {exception.Message}"));
                            store = null; // nothing after this matches the recording until the next snapshot reloads
                        }
                    }
                    else if (replayEvent is BeatEvent beat && store != null && resumed == false)
                    {
                        beatSettings ??= WireJsonSettings.For(store);
                        try
                        {
                            JsonConvert.DeserializeObject<PresentationBeat>(beat.BeatJson, beatSettings);
                            beats++;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(new ReplayVerifyIssue(beat.Seq, "beat", $"The beat at Seq {beat.Seq} does not read back: {exception.Message}"));
                        }
                    }
                }
            }

            return (compared, deltas, beats);
        }

        private static ReplayVerifyIssue Divergence(SnapshotEvent snapshot, SnapshotEvent previous, string builtJson,
            GameDataStore built, Dictionary<(int Type, int Index), long> lastChange)
        {
            GameSaveFile? expected = JsonConvert.DeserializeObject<GameSaveFile>(snapshot.SaveJson);
            GameSaveFile? actual = JsonConvert.DeserializeObject<GameSaveFile>(builtJson);
            string headline = $"Snapshot {snapshot.Index} (Seq {snapshot.Seq}) differs from snapshot {previous.Index} plus the deltas after it";
            if (expected == null || actual == null)
            {
                return new ReplayVerifyIssue(snapshot.Seq, "snapshot", headline + " (and could not be compared entry by entry).");
            }

            var lines = new List<(long Seq, string Text)>();
            if (expected.TypeMap.Count != actual.TypeMap.Count
                || expected.TypeMap.Zip(actual.TypeMap).Any(p => p.First.TypeId != p.Second.TypeId || p.First.Capacity != p.Second.Capacity))
            {
                lines.Add((previous.Seq, "the store's type map or capacities differ"));
            }

            Dictionary<(int, int), ReferenceJsonValuePair> recorded = ByKey(expected.Entries);
            Dictionary<(int, int), ReferenceJsonValuePair> rebuilt = ByKey(actual.Entries);
            foreach ((int Type, int Index) key in recorded.Keys.Union(rebuilt.Keys).OrderBy(k => k))
            {
                bool inRecorded = recorded.TryGetValue(key, out ReferenceJsonValuePair want);
                bool inRebuilt = rebuilt.TryGetValue(key, out ReferenceJsonValuePair got);
                string entry = Describe(built, inRecorded ? want.DataReference : got.DataReference);
                long seq = lastChange.TryGetValue(key, out long changed) ? changed : previous.Seq;
                string when = lastChange.ContainsKey(key) ? $"last change Seq {changed}" : $"no change recorded since snapshot {previous.Index}";

                if (inRecorded == false)
                {
                    lines.Add((seq, $"{entry}: only in the rebuilt state ({when})"));
                }
                else if (inRebuilt == false)
                {
                    lines.Add((seq, $"{entry}: missing from the rebuilt state ({when})"));
                }
                else if (want.DataReference.Generation != got.DataReference.Generation)
                {
                    lines.Add((seq, $"{entry}: generation {got.DataReference.Generation}, recorded {want.DataReference.Generation} ({when})"));
                }
                else if (want.JsonValue != got.JsonValue)
                {
                    IReadOnlyList<string> fields = ReplayJsonDiff.ChangedProperties(want.JsonValue, got.JsonValue);
                    string what = fields.Count == 0 ? "formatting only" : string.Join(", ", fields);
                    lines.Add((seq, $"{entry}: {what} ({when})"));
                }
            }

            if (lines.Count == 0)
            {
                return new ReplayVerifyIssue(snapshot.Seq, "snapshot", headline + " (the entries agree; the save text does not).");
            }

            List<(long Seq, string Text)> ordered = lines.OrderBy(l => l.Seq).ToList();
            return new ReplayVerifyIssue(ordered[0].Seq, "snapshot", $"{headline}: {Count(ordered.Count, "entry", "entries")} differ.",
                ordered.Select(l => l.Text).ToList());
        }

        // --- helpers -----------------------------------------------------------------------------------

        private static bool ResumedBetween(ReplayTimeline timeline, long from, long to) =>
            timeline.Markers.Any(m => m.Marker == EReplayMarker.Resumed && m.Seq > from && m.Seq < to);

        private static Dictionary<(int, int), ReferenceJsonValuePair> ByKey(List<ReferenceJsonValuePair> entries)
        {
            var byKey = new Dictionary<(int, int), ReferenceJsonValuePair>();
            foreach (ReferenceJsonValuePair entry in entries)
            {
                byKey[(entry.DataReference.TypeID.ID, entry.DataReference.Index)] = entry;
            }
            return byKey;
        }

        private static DataReference RefOf(ReplayEvent replayEvent) => replayEvent switch
        {
            StoreAddEvent add => add.Ref,
            StoreUpdateEvent update => update.Ref,
            StoreRemoveEvent remove => remove.Ref,
            _ => throw new ArgumentException($"{replayEvent.Kind} is not a store event.", nameof(replayEvent)),
        };

        /// <summary>"UnitData[3]": the entry's type, by the store's type map, and its slot.</summary>
        internal static string Describe(IReadableGameDataStore store, DataReference reference) =>
            $"{TypeName(store, reference.TypeID.ID)}[{reference.Index}]";

        internal static string TypeName(IReadableGameDataStore store, int typeId)
        {
            List<Type> typeMap = store is GameDataStore gameDataStore ? gameDataStore.GetTypeMap() : new List<Type>();
            return typeId >= 0 && typeId < typeMap.Count ? typeMap[typeId].Name : $"type{typeId}";
        }

        internal static string ShortType(string recordedType)
        {
            string name = recordedType;
            int generic = name.IndexOf('`');
            if (generic >= 0) name = name.Substring(0, generic);
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static string Short(TaskID task) => task.ID.ToString().Substring(0, 8);

        private static string Count(long count, string noun, string? plural = null) =>
            $"{count} {(count == 1 ? noun : plural ?? noun + "s")}";
    }
}
