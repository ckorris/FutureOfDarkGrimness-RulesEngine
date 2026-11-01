namespace FDG.Replay
{
    /// <summary>
    /// What the app sees of a game's replay (plan 4.2 "Exposure"): <c>IFDGGame.Replay</c> and
    /// <c>ILobbyViewModel.Replay</c>. The app never touches a recorder's internals.
    /// </summary>
    public interface IReplaySource
    {
        /// <summary>True from the recording's start until the game is torn down or a recording failure
        /// disabled it. What was recorded before a failure stays exportable.</summary>
        bool IsRecording { get; }

        /// <summary>Line 1 of the file as it will be exported - the game's ID, seed, settings and slots (army names
        /// included, which #452's file names use) - or null when the recording never started.</summary>
        ReplayHeader? Header { get; }

        /// <summary>The <c>.fdgreplay</c> bytes as of now (gzip JSON Lines, plan 4.1). Safe from any thread;
        /// the event list is copied under the recorder's lock and compressed outside it.</summary>
        byte[] ExportGzip();

        ReplaySummary Summary { get; }
    }

    /// <summary>
    /// A cheap at-a-glance description of a recording.
    /// </summary>
    /// <param name="Steps">The steps so far - answered requests that offered a real choice, exactly as
    /// <see cref="DefaultStepFilter"/> picks a replay's steps (#456: the browser, the viewer and a save's summary all
    /// give one number). A request with at most one option, and one that failed, is not counted.</param>
    /// <param name="Rounds">The highest round the recording reached (0 before the main phase).</param>
    /// <param name="Outcome">The GameEnded marker's outcome name, or null while the game is running.</param>
    /// <param name="Winner">On a Win, the winners' names as the GameEnded marker's prose ("Alpha" / "Alpha and
    /// Bravo"); null otherwise, and in files from before it was recorded (#457).</param>
    /// <param name="DurationMs">Milliseconds from the recording's start to its latest event.</param>
    /// <param name="SizeBytes">Approximate UNcompressed size of the event stream (payload characters plus a
    /// per-line allowance) - it ignores JSON escaping, so it reads ~18% under the real raw size (measured on
    /// 2k games, #450). The exported file is the gzip of the stream, ~14-16x smaller than the raw.</param>
    public sealed record ReplaySummary(int Steps, int Rounds, string? Outcome, long DurationMs, long SizeBytes,
        string? Winner = null);

    /// <summary>
    /// The counts behind a <see cref="ReplaySummary"/>, kept event by event - by the recorder as it records, and by
    /// <see cref="ReplayWriter"/> over what it writes (#456: the summary rides the header line, so the replay
    /// browser lists a file from its first line). One implementation, so the two can never disagree.
    /// </summary>
    internal sealed class ReplayTally
    {
        // Rough per-line overhead of the container (Seq, T, Kind and the field names) for SizeBytes.
        private const int ApproxLineOverhead = 48;

        private int _steps;
        private int _highestRound;

        // Per open request: did it offer a real choice? Decided when it is asked, counted when it is answered.
        private readonly Dictionary<FDG.StageResolution.TaskID, bool> _isChoice = new Dictionary<FDG.StageResolution.TaskID, bool>();
        private string? _outcome;
        private string? _winner;
        private long _approxSize;
        private long _lastT;

        public static ReplaySummary Of(IEnumerable<ReplayEvent> events)
        {
            ReplayTally tally = new ReplayTally();
            foreach (ReplayEvent replayEvent in events)
            {
                tally.Add(replayEvent);
            }

            return tally.ToSummary();
        }

        public ReplaySummary ToSummary() => new ReplaySummary(_steps, _highestRound, _outcome, _lastT, _approxSize, _winner);

        public void Add(ReplayEvent replayEvent)
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
                    int? options = DefaultStepFilter.CountOptions(request);
                    _isChoice[request.TaskId] = options == null || options > 1;
                    _approxSize += request.RequestJson.Length + request.RequestType.Length
                        + request.ReplyType.Length + request.DisplayName.Length;
                    break;
                case ReplyEvent reply:
                    if (_isChoice.Remove(reply.TaskId, out bool choice) && choice) _steps++;
                    _approxSize += reply.ReplyJson.Length;
                    break;
                case RequestErrorEvent error:
                    _isChoice.Remove(error.TaskId);
                    _approxSize += error.Message.Length;
                    break;
                case BeatEvent beat:
                    _approxSize += beat.BeatJson.Length;
                    break;
                case LogEvent log:
                    _approxSize += log.Line.Length;
                    break;
                case MarkerEvent marker:
                    // A resume (#453) re-enters a round, which the recording has then reached as surely as a start.
                    if ((marker.Marker == EReplayMarker.RoundStart || marker.Marker == EReplayMarker.GameStart
                            || marker.Marker == EReplayMarker.Resumed)
                        && marker.Round > _highestRound)
                    {
                        _highestRound = marker.Round.Value;
                    }

                    if (marker.Marker == EReplayMarker.GameEnded)
                    {
                        _outcome = marker.Outcome;
                        _winner = marker.WinnerName;
                    }

                    // #453: a resumed recovery save (#187) ended once already; the game is running again.
                    if (marker.Marker == EReplayMarker.Resumed)
                    {
                        _outcome = null;
                        _winner = null;
                    }
                    break;
            }

            _approxSize += ApproxLineOverhead;
            _lastT = replayEvent.T;
        }
    }
}
