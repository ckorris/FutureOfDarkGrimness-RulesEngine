using FDG.Data;
using FDG.Network;
using FDG.Presentation;
using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FDG.Replay
{
    /// <summary>A replay that cannot be played back from where it was asked to go.</summary>
    public sealed class ReplayPlaybackException : Exception
    {
        public ReplayPlaybackException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// What one move of a <see cref="ReplayPlayer"/> applied: the events in <c>(FromSeq, ToSeq]</c>, with the
    /// presentation beats (deserialized against the store as it stood at each beat), log lines and markers among
    /// them, in order.
    /// </summary>
    public sealed class PlaybackSpan
    {
        /// <summary>Exclusive: the last event applied before this span (-1 when it starts at the beginning).</summary>
        public long FromSeq { get; }

        /// <summary>Inclusive: the last event this span applied.</summary>
        public long ToSeq { get; }

        public IReadOnlyList<ReplayEvent> Events { get; }

        public IReadOnlyList<PresentationBeat> Beats { get; }

        public IReadOnlyList<LogEvent> LogLines { get; }

        public IReadOnlyList<MarkerEvent> Markers { get; }

        /// <summary>True when the move rebuilt the store from a snapshot (<see cref="ReplayPlayer.OnStoreReplaced"/>
        /// fired) rather than applying deltas in place.</summary>
        public bool StoreReplaced { get; }

        /// <summary>Beats that could not be read back (ASCII, one line each); playback carries on without them.</summary>
        public IReadOnlyList<string> Problems { get; }

        internal PlaybackSpan(long fromSeq, long toSeq, IReadOnlyList<ReplayEvent> events, IReadOnlyList<PresentationBeat> beats,
            IReadOnlyList<LogEvent> logLines, IReadOnlyList<MarkerEvent> markers, bool storeReplaced, IReadOnlyList<string> problems)
        {
            FromSeq = fromSeq;
            ToSeq = toSeq;
            Events = events;
            Beats = beats;
            LogLines = logLines;
            Markers = markers;
            StoreReplaced = storeReplaced;
            Problems = problems;
        }
    }

    /// <summary>
    /// Plays a recorded game back (plan 4.3, D1): materialises the game state at any step from a snapshot plus
    /// the recorded store deltas - never a stage, a resolver or a die. No renderer either: <see cref="Store"/> and
    /// <see cref="TableState"/> are what a front end draws from.
    /// <para>
    /// <b>Where a step stands.</b> The state AT a step is the state its decision was asked in, before its
    /// effects; those effects play in the span returned when moving on to the next step (see
    /// <see cref="ReplayStep"/>). After the last step, <see cref="StepForward"/> moves to the end of the
    /// recording (<see cref="IsAtEnd"/>): the last decision's effects and the game's end.
    /// </para>
    /// <para>
    /// <b>Moves.</b> <see cref="SeekToStep"/> and <see cref="StepBack"/> rebuild a NEW store from the nearest
    /// usable snapshot and fire <see cref="OnStoreReplaced"/>; <see cref="StepForward"/> applies deltas to the
    /// current store in place and does not, unless it crosses a <c>Resumed</c> marker (the game was reloaded
    /// there, so state is replaced). Anything holding the old <see cref="Store"/> or its bindings must re-read
    /// after <see cref="OnStoreReplaced"/>.
    /// </para>
    /// <para>Single-threaded: call it from one thread (the UI thread), like the store it owns.</para>
    /// </summary>
    public sealed class ReplayPlayer
    {
        public ReplayTimeline Timeline { get; }

        public IReadOnlyList<ReplayStep> Steps => Timeline.Steps;

        public GameDataStore Store { get; private set; }

        public ITableState TableState { get; private set; }

        /// <summary>The step the state stands at, or null at the end of the recording.</summary>
        public ReplayStep? CurrentStep => _stepIndex < Steps.Count ? Steps[_stepIndex] : null;

        /// <summary>True once the whole recording has been applied (past the last step's effects).</summary>
        public bool IsAtEnd => _stepIndex >= Steps.Count;

        /// <summary>Raised after <see cref="Store"/> and <see cref="TableState"/> were replaced by a rebuild.</summary>
        public event Action<GameDataStore>? OnStoreReplaced;

        // Steps.Count = at the end. _cursorSeq = the last event applied to Store.
        private int _stepIndex;
        private long _cursorSeq;

        // Set when an apply threw part-way: the store no longer matches any point of the recording, so the next
        // move rebuilds rather than continuing in place.
        private bool _dirty;

        /// <summary>
        /// Indexes nothing itself - <paramref name="timeline"/> already has - and starts at the first step (or at
        /// the end, for a recording with no steps). Throws <see cref="ReplayPlaybackException"/> when the recording
        /// has no snapshot to start from.
        /// </summary>
        public ReplayPlayer(ReplayTimeline timeline)
        {
            Timeline = timeline;
            Store = null!;      // both set by the first rebuild, below
            TableState = null!;

            if (Steps.Count > 0)
            {
                SeekToStep(0);
            }
            else
            {
                SeekToEnd();
            }
        }

        /// <summary>Rebuilds the state at step <paramref name="index"/>; the span is everything applied on top of
        /// the snapshot.</summary>
        public PlaybackSpan SeekToStep(int index)
        {
            if (index < 0 || index >= Steps.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The replay has {Steps.Count} step(s).");
            }

            PlaybackSpan span = Rebuild(Steps[index].RequestSeq);
            _stepIndex = index;
            return span;
        }

        /// <summary>Applies the whole recording: the state the game ended in.</summary>
        public PlaybackSpan SeekToEnd()
        {
            PlaybackSpan span = Rebuild(LastSeq);
            _stepIndex = Steps.Count;
            return span;
        }

        /// <summary>Moves to the next step (or, from the last step, to the end), applying in place. Null at the end.</summary>
        public PlaybackSpan? StepForward()
        {
            if (IsAtEnd)
            {
                return null;
            }

            int next = _stepIndex + 1;
            long target = next < Steps.Count ? Steps[next].RequestSeq : LastSeq;

            PlaybackSpan span;
            if (_dirty || CrossesResume(_cursorSeq, target))
            {
                span = Rebuild(target);
            }
            else
            {
                long from = _cursorSeq;
                span = ApplyRange(from, target, storeReplaced: false);
            }

            _stepIndex = next;
            return span;
        }

        /// <summary>Moves to the previous step by rebuilding it (always fires <see cref="OnStoreReplaced"/>). From
        /// the end, that is the last step. Null at the first step.</summary>
        public PlaybackSpan? StepBack()
        {
            if (_stepIndex == 0 || Steps.Count == 0)
            {
                return null;
            }

            return SeekToStep(_stepIndex - 1);
        }

        // --- Internals ----------------------------------------------------------------------------------

        private long LastSeq => Timeline.Events.Count > 0 ? Timeline.Events[^1].Seq : -1;

        private PlaybackSpan Rebuild(long target)
        {
            SnapshotEvent snapshot = Timeline.SnapshotFor(target)
                ?? throw new ReplayPlaybackException(Timeline.Snapshots.Count == 0
                    ? "This replay has no snapshot to start from (the recording stopped before the game began)."
                    : $"No snapshot reaches event {target} of this replay (a resume without its snapshot).");

            GameDataStore store;
            try
            {
                store = GameSaveSerializer.Load(snapshot.SaveJson, stampLegacyReserves: false);
            }
            catch (Exception exception)
            {
                throw new ReplayPlaybackException($"Snapshot {snapshot.Index} of this replay could not be loaded: {exception.Message}", exception);
            }

            Store = store;
            TableState = new FDG.TableState(store);
            _cursorSeq = snapshot.Seq;
            _dirty = false;
            OnStoreReplaced?.Invoke(store);

            return ApplyRange(snapshot.Seq, target, storeReplaced: true);
        }

        // Applies every event in (from, to] to Store in Seq order, collecting the span.
        private PlaybackSpan ApplyRange(long from, long to, bool storeReplaced)
        {
            var events = new List<ReplayEvent>();
            var beats = new List<PresentationBeat>();
            var logLines = new List<LogEvent>();
            var markers = new List<MarkerEvent>();
            var problems = new List<string>();
            JsonSerializerSettings? beatSettings = null;

            for (int i = Timeline.IndexOfSeq(from + 1); i < Timeline.Events.Count && Timeline.Events[i].Seq <= to; i++)
            {
                ReplayEvent replayEvent = Timeline.Events[i];
                events.Add(replayEvent);

                switch (replayEvent)
                {
                    case StoreAddEvent:
                    case StoreUpdateEvent:
                    case StoreRemoveEvent:
                        try
                        {
                            StoreDeltaApplier.Apply(Store, replayEvent, EDeltaGenerationPolicy.Adopt);
                        }
                        catch (Exception exception)
                        {
                            _dirty = true;
                            throw new ReplayPlaybackException(
                                $"Replay event {replayEvent.Seq} ({replayEvent.Kind}) could not be applied: {exception.Message}", exception);
                        }
                        break;

                    case BeatEvent beat:
                        beatSettings ??= WireJsonSettings.For(Store);
                        try
                        {
                            PresentationBeat? read = JsonConvert.DeserializeObject<PresentationBeat>(beat.BeatJson, beatSettings);
                            if (read != null) beats.Add(read);
                        }
                        catch (Exception exception)
                        {
                            problems.Add($"Beat at event {beat.Seq} could not be read: {exception.Message}");
                        }
                        break;

                    case LogEvent log:
                        logLines.Add(log);
                        break;

                    case MarkerEvent marker:
                        markers.Add(marker);
                        break;
                }

                _cursorSeq = replayEvent.Seq;
            }

            _cursorSeq = Math.Max(_cursorSeq, to);
            return new PlaybackSpan(from, to, events, beats, logLines, markers, storeReplaced, problems);
        }

        private bool CrossesResume(long from, long to)
        {
            foreach (MarkerEvent marker in Timeline.Markers)
            {
                if (marker.Seq > to) break;
                if (marker.Seq > from && marker.Marker == EReplayMarker.Resumed) return true;
            }
            return false;
        }
    }
}
