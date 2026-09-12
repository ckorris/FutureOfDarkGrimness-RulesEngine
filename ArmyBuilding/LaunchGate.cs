using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 launch gate (decision 9, 2026-07-02): hard legality problems across the lobby's loaded armies.
    // Pure and fixture-free so the lobby view-models can stay thin over it.
    //
    // #400 split it in two, because the lobby now treats the halves differently and asks for them at very
    // different rates:
    //
    //   BlockingProblems    - no army, over points, wrong game system. These BLOCK: the LAUNCH button is
    //                         disabled while any of them stands. Cheap by construction (it reads points
    //                         and the system slug and nothing else), because the lobby evaluates it every
    //                         frame to decide whether the button is live.
    //   OverridableProblems - a Forge army's catalog-validation Errors (force org and friends). These
    //                         still raise the #153 "launch anyway?" confirm rather than blocking, so the
    //                         house-rules escape hatch survives. Runs the full ListValidator over the
    //                         army's embedded book, so the lobby asks for it only on click.
    public static class LaunchGate
    {
        /// <summary>
        /// Problems that must be FIXED before the lobby can launch (#400), one line each:
        /// <list type="bullet">
        ///   <item>a slot with no army assigned at all - before #400 the host quietly substituted a
        ///   100-pt stub, which is how a player ended up in a game with an army they never picked;</item>
        ///   <item>an army over the LOBBY's points limit (the saved list's own limit is builder-time
        ///   advice; the lobby setting is authoritative at launch);</item>
        ///   <item>an army from a game system <paramref name="allowedSystems"/> doesn't accept.</item>
        /// </list>
        /// Empty means nothing is blocking. Deliberately absent: an UNDERBUILT army. Being under the
        /// limit is legal - it is a player giving away budget, which the lobby colours yellow and says
        /// so about, and blocking it would reject a 1000-pt list in a 2000-pt lobby that both players
        /// agreed to.
        /// </summary>
        public static IReadOnlyList<string> BlockingProblems(
            IEnumerable<(string PlayerName, ArmyListFile? Army)> players,
            int lobbyPointsLimit, EAllowedGameSystems allowedSystems)
        {
            var problems = new List<string>();
            foreach ((string playerName, ArmyListFile? army) in players)
            {
                if (army is null)
                {
                    problems.Add($"{playerName}: no army assigned.");
                    continue;
                }

                if (army.TotalPoints > lobbyPointsLimit)
                    problems.Add($"{playerName}: army is {army.TotalPoints} pts, over the {lobbyPointsLimit} pt lobby limit.");

                if (!GameSystems.IsAllowed(allowedSystems, army.GameSystem))
                {
                    problems.Add($"{playerName}: {GameSystems.DisplayName(army.GameSystem)} army, but this "
                        + $"lobby takes {GameSystems.DisplayName(allowedSystems)} armies only.");
                }
            }

            return problems;
        }

        /// <summary>
        /// Problems worth a confirm but not a block: full catalog-validation Errors for Forge-built
        /// armies (a <see cref="BuiltArmyFile"/> whose embedded book + selections survived the trip).
        /// Hand-authored armies have no catalog to validate against and never appear here. Empty means
        /// the launch is clean.
        /// </summary>
        public static IReadOnlyList<string> OverridableProblems(
            IEnumerable<(string PlayerName, ArmyListFile? Army)> players)
        {
            var problems = new List<string>();
            foreach ((string playerName, ArmyListFile? army) in players)
            {
                if (army is not BuiltArmyFile { Book: not null, Selections: not null } built) continue;

                foreach (ListIssue issue in ListValidator.Validate(built.Book, built.Selections, built))
                {
                    // The catalog validator's own points line is builder-time advice against the saved
                    // list's limit — BlockingProblems makes the authoritative call against the lobby's.
                    if (issue.Severity == ListIssueSeverity.Error
                        && !issue.Message.StartsWith("Over points limit"))
                    {
                        problems.Add($"{playerName}: {issue.Message}");
                    }
                }
            }

            return problems;
        }
    }
}
