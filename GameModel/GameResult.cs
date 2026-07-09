using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>How a game finished.</summary>
    public enum EGameOutcome
    {
        /// <summary>Exactly one player held the most objectives.</summary>
        Win,

        /// <summary>No objectives owned, or two or more players tied at the top score.</summary>
        Tie,

        /// <summary>The game did not reach victory calculation (player disconnect, engine fault).</summary>
        Fault,
    }

    /// <summary>
    /// The machine-readable end-of-game record, built by <see cref="Stages.VictoryCalculationStage"/> and
    /// raised on <see cref="GameModel.FDGServer.OnGameCompleted"/>. The long-standing
    /// <see cref="GameModel.FDGServer.OnGameEnded"/> string event still fires alongside it (carrying
    /// <see cref="Message"/>) — that is what the network wire and the front-end banners consume.
    /// <para>
    /// Exists because automated play (benchmarks, self-play training, search rollouts — work item #191)
    /// needs the winner and the score margin as data, not as prose to be parsed back. Host-side only:
    /// nothing here crosses the wire.
    /// </para>
    /// </summary>
    /// <param name="Outcome">Win, Tie, or Fault.</param>
    /// <param name="Winner">The winning player, or null for <see cref="EGameOutcome.Tie"/> / <see cref="EGameOutcome.Fault"/>.</param>
    /// <param name="WinnerName">The winner's display name, or null when there is no winner.</param>
    /// <param name="Scores">Final controlled-objective counts, one entry per filled player slot, in slot order. Empty on Fault.</param>
    /// <param name="RoundsPlayed">Rounds completed, or 0 if the game ended before the main phase began.</param>
    /// <param name="Message">The player-facing sentence ("X wins!", "It's a tie!", or the fault reason). ASCII only.</param>
    public sealed record GameResult(
        EGameOutcome Outcome,
        PlayerID? Winner,
        string? WinnerName,
        IReadOnlyList<PlayerObjectiveScore> Scores,
        int RoundsPlayed,
        string Message)
    {
        public static GameResult ForWin(PlayerID winner, string winnerName,
            IReadOnlyList<PlayerObjectiveScore> scores, int roundsPlayed) =>
            new(EGameOutcome.Win, winner, winnerName, scores, roundsPlayed, $"{winnerName} wins!");

        public static GameResult ForTie(IReadOnlyList<PlayerObjectiveScore> scores, int roundsPlayed) =>
            new(EGameOutcome.Tie, null, null, scores, roundsPlayed, "It's a tie!");

        /// <summary>A game that never reached victory calculation. <paramref name="message"/> must be ASCII.</summary>
        public static GameResult ForFault(string message) =>
            new(EGameOutcome.Fault, null, null, Array.Empty<PlayerObjectiveScore>(), 0, message);

        /// <summary>
        /// One-line ASCII summary for headless logs and benchmark reports, e.g.
        /// <c>outcome=Win winner="Crimson Fists" rounds=4 scores=[2, 1]</c>. Stable and greppable —
        /// automated verification keys off it.
        /// </summary>
        public string ToSummaryLine()
        {
            string winner = WinnerName is null ? "none" : $"\"{WinnerName}\"";
            string scores = string.Join(", ", Scores.Select(s => s.ObjectiveCount));
            return $"outcome={Outcome} winner={winner} rounds={RoundsPlayed} scores=[{scores}]";
        }
    }
}
