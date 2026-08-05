
using FDG.Presentation.Beats;

namespace FDG.Stages
{

    public class VictoryCalculationStage : StageBase<IGameContext>
    {
        public VictoryCalculationStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IGameContext context)
        {
            var objectives = GameContext.TableState.Objectives.Objects.ToList();

            // Final scores and round count come from the live read model, so the structured result agrees
            // with whatever the scoreboard last showed. Scores covers filled player slots in slot order
            // (deterministic); the tally below covers every objective owner, including any player without
            // a slot, which is what decides the winner.
            IReadOnlyList<PlayerObjectiveScore> finalScores = GameContext.TableState.Progress.Scores;
            int roundsPlayed = GameContext.TableState.Progress.RoundCount ?? 0;

            if (objectives.Count == 0)
            {
                await AnnounceTie(context, finalScores, roundsPlayed);
                return;
            }

            // Tally objectives per player.
            var scoreByPlayer = new Dictionary<PlayerID, int>();
            foreach (var obj in objectives)
            {
                if (obj.OwnerID.HasValue)
                {
                    var pid = obj.OwnerID.Value;
                    scoreByPlayer.TryGetValue(pid, out int current);
                    scoreByPlayer[pid] = current + 1;
                }
            }

            foreach (var kv in scoreByPlayer.OrderByDescending(kv => kv.Value))
                GameContext.TextOutput.Log($"  Player {kv.Key.ID}: {kv.Value} objective(s)");

            // #257: the winner is the TEAM with the highest summed objective count, so teammates pool
            // their objectives. #331 moved that arithmetic into TeamScoreTally so the front end can colour
            // a victory celebration by the winning side using the SAME computation rather than a lookalike
            // - the structured result never crosses the wire, but the objectives and slots this reads do.
            var players = GameContext.TableState.Players.Objects.ToList();
            IReadOnlyList<TeamScore> teamScores = TeamScoreTally.Build(players, objectives);

            // Team tally lines only when a real multi-player team exists - keeps 1v1 logs unchanged.
            if (players.GroupBy(p => p.TeamNumber).Any(g => g.Count() > 1))
            {
                foreach (TeamScore score in teamScores.Where(score => score.TeamNumber > int.MinValue / 2))
                    GameContext.TextOutput.Log($"  Team {score.TeamNumber}: {score.ObjectiveCount} objective(s)");
            }

            IReadOnlyList<TeamScore> winningTeams = TeamScoreTally.TopTeams(teamScores);

            if (winningTeams.Count != 1)
            {
                // No objective owned at all, or two or more teams level at the top: both are ties.
                await AnnounceTie(context, finalScores, roundsPlayed);
            }
            else
            {
                TeamScore winningTeam = winningTeams[0];

                // The victory text names every player on the winning team (including teammates who held
                // no objective themselves), never just the team number.
                var roster = players.Where(p => p.TeamNumber == winningTeam.TeamNumber)
                    .OrderBy(p => p.SlotID).ToList();
                IReadOnlyList<PlayerID> winnerIds;
                IReadOnlyList<string> winnerNames;
                if (roster.Count > 0)
                {
                    winnerIds = roster.Select(p => p.PlayerID).ToList();
                    winnerNames = roster.Select(p => p.Name).ToList();
                }
                else
                {
                    // A pseudo-team: the winning objective owner has no slot, so no name exists. The tally
                    // still carries the owner itself as that team's one-player roster.
                    winnerIds = winningTeam.Players;
                    winnerNames = new[] { "A player" };
                }

                GameResult result = GameResult.ForWin(winnerIds, winnerNames, finalScores, roundsPlayed);
                await context.Announce(result.Message, new TextColor(255, 215, 0, 255),
                    EBannerTier.Headline);
                GameContext.NotifyGameCompleted(result);
            }
        }

        private async Task AnnounceTie(IGameContext context, IReadOnlyList<PlayerObjectiveScore> finalScores,
            int roundsPlayed)
        {
            GameResult result = GameResult.ForTie(finalScores, roundsPlayed);
            await context.Announce(result.Message, tier: EBannerTier.Headline);
            GameContext.NotifyGameCompleted(result);
        }

        public override void Exit()
        {
        }
    }
}