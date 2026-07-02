using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 launch gate (decision 9, 2026-07-02): hard legality problems across the lobby's loaded armies,
    // shown in a host-side "launch anyway?" confirm rather than hard-blocking (house rules stay possible;
    // Cancel is the default). Pure and fixture-free so the lobby view-models can stay thin over it.
    public static class LaunchGate
    {
        /// <summary>
        /// Problems that should stop a silent launch: an army over the LOBBY's points limit (the saved
        /// list's own limit is builder-time advice; the lobby setting is authoritative at launch), plus
        /// full catalog-validation Errors for Forge-built armies (a <see cref="BuiltArmyFile"/> whose
        /// embedded book + selections survived the trip). Hand-authored armies only get the points check.
        /// Empty means launch clean.
        /// </summary>
        public static IReadOnlyList<string> ValidateArmies(
            IEnumerable<(string PlayerName, ArmyListFile? Army)> players, int lobbyPointsLimit)
        {
            var problems = new List<string>();
            foreach ((string playerName, ArmyListFile? army) in players)
            {
                if (army is null) continue;

                if (army.TotalPoints > lobbyPointsLimit)
                    problems.Add($"{playerName}: army is {army.TotalPoints} pts, over the {lobbyPointsLimit} pt lobby limit.");

                if (army is BuiltArmyFile { Book: not null, Selections: not null } built)
                {
                    foreach (ListIssue issue in ListValidator.Validate(built.Book, built.Selections, built))
                    {
                        // The catalog validator's own points line is builder-time advice against the saved
                        // list's limit — the lobby check above is the authoritative one at launch.
                        if (issue.Severity == ListIssueSeverity.Error
                            && !issue.Message.StartsWith("Over points limit"))
                        {
                            problems.Add($"{playerName}: {issue.Message}");
                        }
                    }
                }
            }

            return problems;
        }
    }
}
