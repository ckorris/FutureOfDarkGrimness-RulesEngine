using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>How a game finished.</summary>
    public enum EGameOutcome
    {
        /// <summary>Exactly one team held the most objectives, summed over its players (#257).</summary>
        Win,

        /// <summary>No objectives owned, or two or more teams tied at the top summed score.</summary>
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
    /// <param name="Winner">The first winning-team player in slot order (#257: the whole roster is in
    /// <paramref name="WinnerPlayers"/>; for single-player teams this is simply the winner, which keeps
    /// 1v1 consumers like FdgLab's winner-slot mapping unchanged), or null for
    /// <see cref="EGameOutcome.Tie"/> / <see cref="EGameOutcome.Fault"/>.</param>
    /// <param name="WinnerName">The winning players' display names joined for prose ("Alpha" /
    /// "Alpha and Bravo"), or null when there is no winner.</param>
    /// <param name="WinnerPlayers">Every player on the winning team, in slot order. Empty on Tie/Fault.</param>
    /// <param name="Scores">Final controlled-objective counts, one entry per filled player slot, in slot
    /// order. Per PLAYER - the winner is decided on per-team sums (#257), but the margin data stays
    /// per player.</param>
    /// <param name="RoundsPlayed">Rounds completed, or 0 if the game ended before the main phase began.</param>
    /// <param name="Message">The player-facing sentence ("X wins!", "X and Y win!", "It's a tie!", or the
    /// fault reason). Always names the players, never a bare team number. ASCII only.</param>
    public sealed record GameResult(
        EGameOutcome Outcome,
        PlayerID? Winner,
        string? WinnerName,
        IReadOnlyList<PlayerID> WinnerPlayers,
        IReadOnlyList<PlayerObjectiveScore> Scores,
        int RoundsPlayed,
        string Message)
    {
        /// <summary>#257: a win for a whole team. Both lists are the winning team's players in slot
        /// order; the message names all of them ("Alpha and Bravo win!").</summary>
        public static GameResult ForWin(IReadOnlyList<PlayerID> winnerPlayers, IReadOnlyList<string> winnerNames,
            IReadOnlyList<PlayerObjectiveScore> scores, int roundsPlayed)
        {
            string joined = JoinNames(winnerNames);
            string verb = winnerNames.Count == 1 ? "wins" : "win";
            return new(EGameOutcome.Win, winnerPlayers[0], joined, winnerPlayers, scores, roundsPlayed,
                $"{joined} {verb}!");
        }

        public static GameResult ForTie(IReadOnlyList<PlayerObjectiveScore> scores, int roundsPlayed) =>
            new(EGameOutcome.Tie, null, null, Array.Empty<PlayerID>(), scores, roundsPlayed, "It's a tie!");

        /// <summary>A game that never reached victory calculation. <paramref name="message"/> must be ASCII.</summary>
        public static GameResult ForFault(string message) =>
            new(EGameOutcome.Fault, null, null, Array.Empty<PlayerID>(),
                Array.Empty<PlayerObjectiveScore>(), 0, message);

        // "Alpha" / "Alpha and Bravo" / "Alpha, Bravo and Charlie". ASCII only.
        private static string JoinNames(IReadOnlyList<string> names) =>
            names.Count == 1
                ? names[0]
                : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

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
