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
    /// </summary>
    public sealed class ReplayRecorder : IReplaySource, IRequestObserver, IBeatObserver, IDisposable
    {
        // Rough per-line overhead of the container (Seq, T, Kind and the field names) for SizeBytes.
        private const int ApproxLineOverhead = 48;

        private readonly object _lock = new object();
        private readonly List<ReplayEvent> _events = new List<ReplayEvent>();
        private readonly Stopwatch _clock = new Stopwatch();

        private ReplayHeader? _header;
        private long _nextSeq;
        private int _nextSnapshotIndex;
        private bool _recording;
        private bool _failed;
        private bool _disposed;

        // Summary tallies, kept as events are appended so Summary never walks the list.
        private int _replies;
        private int _highestRound;
        private string? _outcome;
        private long _approxSize;

        private ITextOutput _failureOutput = new ConsoleTextOutput();
        private IReadableGameDataStore? _attachedStore;
        private JsonSerializerSettings _beatSettings = StorelessBeatSettings();

        /// <summary>Test seam: how a snapshot's save JSON is produced. A throwing serializer is how the
        /// tests exercise the disable-on-failure path.</summary>
        internal Func<GameDataStore, string> SnapshotSerializer { get; set; } = GameSaveSerializer.Save;

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

        /// <summary>
        /// Begins the recording: <c>T</c> counts from here. <paramref name="failureOutput"/> is where the one
        /// failure line goes - the server's player-facing log, but NOT the recording log decorator (a failure
        /// while recording a log line must not recurse into recording). Starting twice is a wiring error.
        /// </summary>
        public void Start(ReplayHeader header, ITextOutput? failureOutput = null)
        {
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

                _header = header;
                _failureOutput = failureOutput ?? _failureOutput;
                _recording = true;
                _clock.Start();
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

            string saveJson;
            try
            {
                saveJson = SnapshotSerializer(store);
            }
            catch (Exception exception)
            {
                Fail("snapshot", exception);
                return index;
            }

            Append(() => new SnapshotEvent(index, saveJson), "snapshot");
            return index;
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

        // --- Log and markers ----------------------------------------------------------------------------

        public void RecordLog(string line, bool debug) =>
            Append(() => new LogEvent(line) { Debug = debug }, "log line");

        /// <summary>Appends a marker; <c>Seq</c>/<c>T</c> on <paramref name="marker"/> are ignored and
        /// assigned here.</summary>
        public void RecordMarker(MarkerEvent marker) => Append(() => marker, "marker");

        // --- Export -------------------------------------------------------------------------------------

        public byte[] ExportGzip()
        {
            ReplayHeader header;
            ReplayEvent[] events;
            lock (_lock)
            {
                header = _header ?? throw new InvalidOperationException("Nothing to export: the replay recorder was never started.");
                events = _events.ToArray();
            }

            using MemoryStream stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, events, stream);
            return stream.ToArray();
        }

        public ReplaySummary Summary
        {
            get
            {
                lock (_lock)
                {
                    long duration = _events.Count > 0 ? _events[_events.Count - 1].T : 0;
                    return new ReplaySummary(_replies, _highestRound, _outcome, duration, _approxSize);
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

                    ReplayEvent replayEvent = build() with { Seq = _nextSeq++, T = _clock.ElapsedMilliseconds };
                    _events.Add(replayEvent);
                    Tally(replayEvent);
                }
            }
            catch (Exception exception)
            {
                Fail(what, exception);
            }
        }

        // Under _lock.
        private void Tally(ReplayEvent replayEvent)
        {
            switch (replayEvent)
            {
                case StoreAddEvent add:
                    _approxSize += add.Json.Length;
                    break;
                case StoreUpdateEvent update:
                    _approxSize += update.Json.Length;
                    break;
                case SnapshotEvent snapshot:
                    _approxSize += snapshot.SaveJson.Length;
                    break;
                case RequestEvent request:
                    _approxSize += request.RequestJson.Length + request.RequestType.Length
                        + request.ReplyType.Length + request.DisplayName.Length;
                    break;
                case ReplyEvent reply:
                    _replies++;
                    _approxSize += reply.ReplyJson.Length;
                    break;
                case RequestErrorEvent error:
                    _approxSize += error.Message.Length;
                    break;
                case BeatEvent beat:
                    _approxSize += beat.BeatJson.Length;
                    break;
                case LogEvent log:
                    _approxSize += log.Line.Length;
                    break;
                case MarkerEvent marker:
                    if ((marker.Marker == EReplayMarker.RoundStart || marker.Marker == EReplayMarker.GameStart)
                        && marker.Round > _highestRound)
                    {
                        _highestRound = marker.Round.Value;
                    }

                    if (marker.Marker == EReplayMarker.GameEnded)
                    {
                        _outcome = marker.Outcome;
                    }
                    break;
            }

            _approxSize += ApproxLineOverhead;
        }

        private void Fail(string what, Exception exception)
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

            try
            {
                _failureOutput.Log(Ascii($"[replay] Recording stopped: a {what} could not be recorded " +
                    $"({exception.GetType().Name}: {exception.Message}). This game's replay ends here."));
            }
            catch
            {
                // The failure line is best effort; the recorder must never throw into the engine.
            }
        }

        // Game text is ASCII-only (the ImGui atlas renders anything else as '?'); exception text may not be.
        private static string Ascii(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] > '~' || (chars[i] < ' ' && chars[i] != '\n'))
                {
                    chars[i] = '?';
                }
            }

            return new string(chars);
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
