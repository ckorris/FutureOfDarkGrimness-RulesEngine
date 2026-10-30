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

        /// <summary>The <c>.fdgreplay</c> bytes as of now (gzip JSON Lines, plan 4.1). Safe from any thread;
        /// the event list is copied under the recorder's lock and compressed outside it.</summary>
        byte[] ExportGzip();

        ReplaySummary Summary { get; }
    }

    /// <summary>
    /// A cheap at-a-glance description of a recording.
    /// </summary>
    /// <param name="Steps">Answered requests so far (a Request with its Reply). #454's step filter folds
    /// the single-option ones on top of this; this count does not.</param>
    /// <param name="Rounds">The highest round the recording reached (0 before the main phase).</param>
    /// <param name="Outcome">The GameEnded marker's outcome name, or null while the game is running.</param>
    /// <param name="DurationMs">Milliseconds from the recording's start to its latest event.</param>
    /// <param name="SizeBytes">Approximate UNcompressed size of the event stream (payload characters plus a
    /// per-line allowance). The exported file is the gzip of this, typically 15-20x smaller.</param>
    public sealed record ReplaySummary(int Steps, int Rounds, string? Outcome, long DurationMs, long SizeBytes);
}
