using System.Diagnostics;
using FDG.Data;
using FDG.Network;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StageResolution;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FDG.Replay
{
    /// <summary>
    /// The host-side replay recorder (#450, plan 4.2): buffers, in memory and in order, every store delta,
    /// every request / reply / request error, every presentation beat, every game-log line, the flow markers
    /// and periodic full snapshots of one game, and exports them as the <c>.fdgreplay</c> container.
    /// <para>
    /// <b>Opt-in.</b> The real-game launch paths construct one and hand it to <c>FDGServer</c>, which wires it
    /// up; simulations, FdgLab and tests pass nothing and nothing is recorded.
    /// </para>
    /// <para>
    /// <b>Invisible to the game.</b> Recording is a list append under one lock - no I/O, no serialization
    /// beyond the beat and snapshot JSON, which are built outside the lock. Nothing here throws into the
    /// engine: the first failure is logged once (with a <c>[replay]</c> prefix) and turns recording off for
    /// the rest of the game; what was recorded before it stays exportable (the stream simply ends early, with
    /// no GameEnded marker).
    /// </para>
    /// <para>
    /// <b>Threads.</b> Store events, beats, log lines, markers and requests arrive on the engine thread,
    /// replies on the bus / network read thread, <see cref="ExportGzip"/> and <see cref="Summary"/> from the
    /// UI thread. <see cref="Seq"/> is assigned under the lock, so it is dense and matches append order.
    /// </para>
    /// <para>
    /// <b>Continuing a saved game (#453).</b> A save embeds the recording so far, ending with a <c>Saved</c> marker
    /// (<c>FDGServer.SaveGame</c>). <see cref="Continue"/> seeds a recorder with it: the earlier events stay as they
    /// were, <c>Seq</c>, <c>T</c> and the snapshot index count on from them, and the resume adds a <c>Resumed</c>
    /// marker and a fresh snapshot. That snapshot REPLACES the state - the activation under way at the save is
    /// re-played from its start after the resume (the #052 save point is the activation boundary), so the deltas
    /// between its <c>ActivationStart</c> and <c>Saved</c> describe work the resume undid, and playback never
    /// applies anything across a <c>Resumed</c> (plan 4.1).
    /// </para>
    /// </summary>
    public sealed class ReplayRecorder : IReplaySource, IRequestObserver, IBeatObserver, IDisposable
    {
        private readonly object _lock = new object();
        private readonly List<ReplayEvent> _events = new List<ReplayEvent>();
        private readonly Stopwatch _clock = new Stopwatch();

        private ReplayHeader? _header;
        private long _nextSeq;
        private int _nextSnapshotIndex;
        private bool _recording;
        private bool _failed;
        private bool _disposed;

        // #453: set by Continue. _continues makes the resume record Resumed rather than GameStart, whether or not the
        // earlier recording could be kept; _continuedHeader is that recording's header (null if it could not); a
        // problem found while reading it is reported once the failure output is known, at Start. _clockOffset is
        // the T the seeded events reached, so T keeps counting play time across the gap between sessions.
        private bool _continues;
        private ReplayHeader? _continuedHeader;
        private string? _continueProblem;
        private long _clockOffset;

        // Summary counts, kept as events are appended so Summary never walks the list.
        private ReplayTally _tally = new ReplayTally();

        private ITextOutput _failureOutput = new ConsoleTextOutput();
        private IReadableGameDataStore? _attachedStore;
        private JsonSerializerSettings _beatSettings = StorelessBeatSettings();

        /// <summary>Test seam: how a snapshot's save JSON is produced. A throwing serializer is how the
        /// tests exercise the disable-on-failure path.</summary>
        internal Func<GameDataStore, string> SnapshotSerializer { get; set; } = store => GameSaveSerializer.Save(store);

        /// <summary>Test seam: replaces the beat serializer (null = the wire settings).</summary>
        internal Func<PresentationBeat, string>? BeatSerializerOverride { get; set; }

        public bool IsRecording
        {
            get { lock (_lock) { return _recording; } }
        }

        /// <summary>The header as started, or null before <see cref="Start"/>.</summary>
        public ReplayHeader? Header
        {
            get { lock (_lock) { return _header; } }
        }

        /// <summary>#453: this recorder continues a saved game's recording (<see cref="Continue"/>), so the resume
        /// records <c>Resumed</c>, not <c>GameStart</c>.</summary>
        internal bool ContinuesASave
        {
            get { lock (_lock) { return _continues; } }
        }

        /// <summary>
        /// #453: the recorder for a resumed save. <paramref name="priorGzip"/> is the save's embedded replay
        /// (<c>GameSaveSerializer.LoadWithReplay</c>); null - a scenario, a save from before #453 or of an unrecorded
        /// game - gives a plain recorder whose resume records <c>GameStart</c>, as before.
        /// <para>Otherwise the earlier events are kept and <see cref="Start"/> keeps their header (game ID, seed, slots,
        /// first start), adding this resume to <see cref="ReplayHeader.ResumedUtc"/>. Bytes this build cannot read (a
        /// format bump between the save and the load, a damaged file), or a replay of a different game than the one
        /// resumed, never block the resume: the recording starts afresh at the resume, with its <c>Resumed</c>
        /// marker, and says so once in the game log.</para>
        /// </summary>
        public static ReplayRecorder Continue(byte[]? priorGzip)
        {
            ReplayRecorder recorder = new ReplayRecorder();
            if (priorGzip == null)
            {
                return recorder;
            }

            recorder._continues = true;
            try
            {
                ReplayDocument prior = ReadPrior(priorGzip);
                prior.Header.Summary = null; // the file's; this recording's is its own (#456)
                lock (recorder._lock)
                {
                    recorder._continuedHeader = prior.Header;
                    recorder.Seed(prior.Events);
                }
            }
            catch (Exception exception)
            {
                recorder._continueProblem = $"[replay] The save's replay could not be read ({exception.Message}). " +
                    "This game's replay starts at the resume.";
            }

            return recorder;
        }

        /// <summary>
        /// #453, for <c>--at STEP --make-save</c>: the recording in <paramref name="document"/> cut before the event
        /// <paramref name="beforeSeq"/> and closed with a <c>Saved</c> marker - what a save taken at that point would
        /// have embedded - for <c>GameSaveSerializer.Save</c>. Not a live recorder: it is closed when returned.
        /// </summary>
        public static ReplayRecorder CutForSave(ReplayDocument document, long beforeSeq)
        {
            ReplayRecorder recorder = new ReplayRecorder();
            int? round = null;
            lock (recorder._lock)
            {
                List<ReplayEvent> kept = document.Events.Where(replayEvent => replayEvent.Seq < beforeSeq).ToList();
                recorder.Seed(kept);
                recorder._header = document.Header;
                recorder._recording = true;
                round = kept.OfType<MarkerEvent>().LastOrDefault(marker => marker.Round != null)?.Round;
            }

            recorder.RecordMarker(new MarkerEvent(EReplayMarker.Saved) { Round = round });
            recorder.Dispose();
            return recorder;
        }

        /// <summary>#453: reads an earlier recording to continue, with the same checks playback makes (format version,
        /// event order). Throws <see cref="ReplayFormatException"/> on anything this build cannot continue.</summary>
        internal static ReplayDocument ReadPrior(byte[] gzip)
        {
            using MemoryStream stream = new MemoryStream(gzip);
            ReplayDocument prior = ReplayReader.ReadGzip(stream);
            _ = new ReplayTimeline(prior);
            return prior;
        }

        /// <summary>#453: takes <paramref name="events"/> as this recording's beginning - before <see cref="Start"/>,
        /// under <c>_lock</c>. Seq, T and the snapshot index count on from them; Summary includes them.</summary>
        internal void Seed(IEnumerable<ReplayEvent> events)
        {
            if (_header != null || _events.Count > 0)
            {
                throw new InvalidOperationException("Only a recorder that has not started can be seeded.");
            }

            foreach (ReplayEvent replayEvent in events)
            {
                _events.Add(replayEvent);
                _tally.Add(replayEvent);
                if (replayEvent is SnapshotEvent snapshot)
                {
                    _nextSnapshotIndex = Math.Max(_nextSnapshotIndex, snapshot.Index + 1);
                }
            }

            if (_events.Count > 0)
            {
                _nextSeq = _events[_events.Count - 1].Seq + 1;
                _clockOffset = _events[_events.Count - 1].T;
            }
        }

        // Under _lock: back to an empty recording (a continued recording of the wrong game).
        private void ClearSeed()
        {
            _events.Clear();
            _continuedHeader = null;
            _nextSeq = 0;
            _nextSnapshotIndex = 0;
            _clockOffset = 0;
            _tally = new ReplayTally();
        }

        /// <summary>
        /// Begins the recording: <c>T</c> counts from here. <paramref name="failureOutput"/> is where the one
        /// failure line goes - the server's player-facing log, but NOT the recording log decorator (a failure
        /// while recording a log line must not recurse into recording). Starting twice is a wiring error.
        /// </summary>
        public void Start(ReplayHeader header, ITextOutput? failureOutput = null)
        {
            string? problem;
            lock (_lock)
            {
                if (_header != null)
                {
                    throw new InvalidOperationException("This replay recorder was already started; use one recorder per game.");
                }

                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ReplayRecorder));
                }

                // #453: a continued recording keeps the header it began with - its game ID, seed, slots and first
                // start - so long as it is this game's.
                problem = _continueProblem;
                if (_continuedHeader != null)
                {
                    if (_continuedHeader.GameId == header.GameId)
                    {
                        header = _continuedHeader;
                    }
                    else
                    {
                        problem = $"[replay] The save's replay is of another game (ID {_continuedHeader.GameId}, " +
                            $"not {header.GameId}). This game's replay starts at the resume.";
                        ClearSeed();
                    }
                }

                if (_continues)
                {
                    header.ResumedUtc.Add(DateTime.UtcNow);
                }

                _header = header;
                _failureOutput = failureOutput ?? _failureOutput;
                _recording = true;
                _clock.Start();
            }

            if (problem != null)
            {
                Note(problem);
            }
        }

        /// <summary>
        /// Subscribes to the store's three JSON events exactly as <c>GameDataUpdateSender</c> does (the same
        /// strings it sends to clients, so no extra serialization), and adopts the store's wire settings for
        /// beats. Undone by <see cref="Dispose"/>.
        /// </summary>
        public void AttachStore(IReadableGameDataStore store)
        {
            lock (_lock)
            {
                if (_attachedStore != null)
                {
                    throw new InvalidOperationException("This replay recorder is already attached to a store.");
                }

                _attachedStore = store;
            }

            _beatSettings = WireJsonSettings.For(store);
            store.OnDataAddedAsJson += RecordStoreAdd;
            store.OnDataUpdatedAsJson += RecordStoreUpdate;
            store.OnDataRemoved += RecordStoreRemove;
        }

        // --- Store ------------------------------------------------------------------------------------

        public void RecordStoreAdd(DataReference reference, string json) =>
            Append(() => new StoreAddEvent(reference, json), "store add");

        public void RecordStoreUpdate(DataReference reference, string json) =>
            Append(() => new StoreUpdateEvent(reference, json), "store update");

        public void RecordStoreRemove(DataReference reference) =>
            Append(() => new StoreRemoveEvent(reference), "store remove");

        // --- Snapshots ---------------------------------------------------------------------------------

        /// <summary>
        /// Appends a full <c>GameSaveSerializer.Save</c> of <paramref name="store"/> and returns its index.
        /// Called at stage boundaries on the engine thread - the only thread that mutates the store - so the
        /// save matches the deltas around it. The index counts every call, recorded or not, so a marker
        /// broadcast carrying it stays aligned with what clients count.
        /// </summary>
        public int Snapshot(GameDataStore store)
        {
            int index;
            lock (_lock)
            {
                index = _nextSnapshotIndex++;
                if (_recording == false)
                {
                    return index;
                }
            }

            RecordSnapshot(store, index);
            return index;
        }

        /// <summary>
        /// #451: a snapshot under an index someone else counted - a client takes one at each of the host's
        /// snapshot cues, under the host's index, so both recordings number a round's snapshot alike. Later
        /// <see cref="Snapshot(GameDataStore)"/> calls count on from it.
        /// </summary>
        internal void Snapshot(GameDataStore store, int index)
        {
            lock (_lock)
            {
                _nextSnapshotIndex = Math.Max(_nextSnapshotIndex, index + 1);
                if (_recording == false)
                {
                    return;
                }
            }

            RecordSnapshot(store, index);
        }

        private void RecordSnapshot(GameDataStore store, int index)
        {
            string saveJson;
            try
            {
                saveJson = SnapshotSerializer(store);
            }
            catch (Exception exception)
            {
                Fail("snapshot", exception);
                return;
            }

            Append(() => new SnapshotEvent(index, saveJson), "snapshot");
        }

        // --- Requests (IRequestObserver) ----------------------------------------------------------------

        public void OnRequested(TaskID taskId, PlayerID playerId, string requestType, string replyType,
            string displayName, string requestJson) =>
            Append(() => new RequestEvent(taskId, playerId, requestType, replyType, displayName, requestJson), "request");

        public void OnReplied(TaskID taskId, string replyJson) =>
            Append(() => new ReplyEvent(taskId, replyJson), "reply");

        public void OnFailed(TaskID taskId, string message) =>
            Append(() => new RequestErrorEvent(taskId, message), "request error");

        // --- Beats (IBeatObserver) ----------------------------------------------------------------------

        public void OnBeat(PresentationBeat beat)
        {
            if (IsRecording == false)
            {
                return;
            }

            string beatJson;
            try
            {
                // Same settings PresentBeatMessage rides the wire with, declared as the base type so the root
                // carries its $type and reads back as the concrete beat.
                beatJson = BeatSerializerOverride != null
                    ? BeatSerializerOverride(beat)
                    : JsonConvert.SerializeObject(beat, typeof(PresentationBeat), _beatSettings);
            }
            catch (Exception exception)
            {
                Fail("beat", exception);
                return;
            }

            Append(() => new BeatEvent(beatJson), "beat");
        }

        /// <summary>#451: a beat already serialized the way <see cref="OnBeat"/> would (the client re-serializes
        /// the one it was sent with the same wire settings).</summary>
        internal void RecordBeatJson(string beatJson) => Append(() => new BeatEvent(beatJson), "beat");

        // --- Log and markers ----------------------------------------------------------------------------

        public void RecordLog(string line, bool debug) =>
            Append(() => new LogEvent(line) { Debug = debug }, "log line");

        /// <summary>Appends a marker; <c>Seq</c>/<c>T</c> on <paramref name="marker"/> are ignored and
        /// assigned here.</summary>
        public void RecordMarker(MarkerEvent marker) => Append(() => marker, "marker");

        // --- Export -------------------------------------------------------------------------------------

        /// <summary>
        /// #453: the recording through its latest <c>Resumed</c> marker - the history a resumed game's clients seed
        /// their own recordings with (<c>LaunchGameMessage.ReplayHistory</c>), so theirs continue it too and agree
        /// with the host's event for event up to the resume. Null when the recording did not begin at a resume.
        /// </summary>
        internal byte[]? ExportThroughResume()
        {
            ReplayHeader header;
            ReplayEvent[] events;
            lock (_lock)
            {
                int resumed = _events.FindLastIndex(replayEvent => replayEvent is MarkerEvent { Marker: EReplayMarker.Resumed });
                if (_header == null || resumed < 0)
                {
                    return null;
                }

                header = _header;
                events = _events.GetRange(0, resumed + 1).ToArray();
            }

            using MemoryStream stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, events, stream);
            return stream.ToArray();
        }

        public byte[] ExportGzip()
        {
            ReplayHeader header;
            ReplayEvent[] events;
            ReplaySummary summary;
            lock (_lock)
            {
                header = _header ?? throw new InvalidOperationException("Nothing to export: the replay recorder was never started.");
                events = _events.ToArray();
                // #457: the running tally has counted exactly these events (Append and Seed add to both under this
                // lock), so the header's summary need not be recounted from them.
                summary = _tally.ToSummary();
            }

            using MemoryStream stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, events, stream, summary);
            return stream.ToArray();
        }

        public ReplaySummary Summary
        {
            get
            {
                lock (_lock)
                {
                    return _tally.ToSummary();
                }
            }
        }

        /// <summary>A copy of the events recorded so far (tests and the #450 measurement).</summary>
        internal IReadOnlyList<ReplayEvent> EventsSnapshot()
        {
            lock (_lock)
            {
                return _events.ToArray();
            }
        }

        /// <summary>Detaches from the store and stops recording. What was recorded stays exportable - the
        /// app writes the file after the game (and its server) are torn down.</summary>
        public void Dispose()
        {
            IReadableGameDataStore? store;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _recording = false;
                _clock.Stop();
                store = _attachedStore;
            }

            if (store != null)
            {
                store.OnDataAddedAsJson -= RecordStoreAdd;
                store.OnDataUpdatedAsJson -= RecordStoreUpdate;
                store.OnDataRemoved -= RecordStoreRemove;
            }
        }

        // --- Internals ----------------------------------------------------------------------------------

        private void Append(Func<ReplayEvent> build, string what)
        {
            try
            {
                lock (_lock)
                {
                    if (_recording == false)
                    {
                        return;
                    }

                    ReplayEvent replayEvent = build() with { Seq = _nextSeq++, T = _clockOffset + _clock.ElapsedMilliseconds };
                    _events.Add(replayEvent);
                    _tally.Add(replayEvent);
                }
            }
            catch (Exception exception)
            {
                Fail(what, exception);
            }
        }

        internal void Fail(string what, Exception exception)
        {
            lock (_lock)
            {
                if (_failed)
                {
                    return;
                }

                _failed = true;
                _recording = false;
            }

            Note($"[replay] Recording stopped: a {what} could not be recorded " +
                $"({exception.GetType().Name}: {exception.Message}). This game's replay ends here.");
        }

        /// <summary>One line to the failure output (the game log), never throwing.</summary>
        internal void Note(string line)
        {
            try
            {
                _failureOutput.Log(ReplayText.Ascii(line));
            }
            catch
            {
                // The failure line is best effort; the recorder must never throw into the engine.
            }
        }

        // Before AttachStore (scripted use): the wire's binder and enum policy without the store's
        // DataBinding converters, which no beat needs (beats carry IDs and positions, not bindings).
        private static JsonSerializerSettings StorelessBeatSettings() => new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new AllowlistSerializationBinder(),
            Converters = { new StringEnumConverter() },
        };
    }

    /// <summary>
    /// Decorates the <see cref="ITextOutput"/> the server hands to the stages: every line goes on to the
    /// players exactly as before, and is also recorded (#450). The Debug category is preserved.
    /// </summary>
    public sealed class ReplayTextOutput : ITextOutput
    {
        private readonly ITextOutput _inner;
        private readonly ReplayRecorder _recorder;

        public ReplayTextOutput(ITextOutput inner, ReplayRecorder recorder)
        {
            _inner = inner;
            _recorder = recorder;
        }

        public void Log(string message, TextColor? color = null)
        {
            _inner.Log(message, color);
            _recorder.RecordLog(message, debug: false);
        }

        public void LogDebug(string message, TextColor? color = null)
        {
            _inner.LogDebug(message, color);
            _recorder.RecordLog(message, debug: true);
        }
    }
}
