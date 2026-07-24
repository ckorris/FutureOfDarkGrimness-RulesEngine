
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
            // their objectives. Each player maps to their slot's team; an objective owner with no slot
            // keeps a private bucket (mirrors the old per-player behavior for that edge, and the pseudo
            // keys count down from int.MinValue so they can't collide with real team numbers).
            var players = GameContext.TableState.Players.Objects.ToList();
            var teamOfPlayer = new Dictionary<PlayerID, int>();
            foreach (var slot in players)
                teamOfPlayer[slot.PlayerID] = slot.TeamNumber;

            int nextPseudoTeam = int.MinValue;
            var scoreByTeam = new Dictionary<int, int>();
            foreach (var obj in objectives) // objective order: deterministic, and covers every owner.
            {
                if (obj.OwnerID.HasValue == false) continue;
                var pid = obj.OwnerID.Value;
                if (teamOfPlayer.TryGetValue(pid, out int team) == false)
                    teamOfPlayer[pid] = team = nextPseudoTeam++;
                scoreByTeam.TryGetValue(team, out int current);
                scoreByTeam[team] = current + 1;
            }

            // Team tally lines only when a real multi-player team exists - keeps 1v1 logs unchanged.
            if (players.GroupBy(p => p.TeamNumber).Any(g => g.Count() > 1))
            {
                foreach (var kv in scoreByTeam.Where(kv => kv.Key > int.MinValue / 2).OrderByDescending(kv => kv.Value))
                    GameContext.TextOutput.Log($"  Team {kv.Key}: {kv.Value} objective(s)");
            }

            int topScore = scoreByTeam.Values.DefaultIfEmpty(0).Max();
            var winningTeams = scoreByTeam.Where(kv => kv.Value == topScore).Select(kv => kv.Key).ToList();

            if (winningTeams.Count == 0 || topScore == 0)
            {
                await AnnounceTie(context, finalScores, roundsPlayed);
            }
            else if (winningTeams.Count > 1)
            {
                await AnnounceTie(context, finalScores, roundsPlayed);
            }
            else
            {
                int winningTeam = winningTeams[0];

                // The victory text names every player on the winning team (including teammates who held
                // no objective themselves), never just the team number.
                var roster = players.Where(p => p.TeamNumber == winningTeam).OrderBy(p => p.SlotID).ToList();
                IReadOnlyList<PlayerID> winnerIds;
                IReadOnlyList<string> winnerNames;
                if (roster.Count > 0)
                {
                    winnerIds = roster.Select(p => p.PlayerID).ToList();
                    winnerNames = roster.Select(p => p.Name).ToList();
                }
                else
                {
                    // A pseudo-team: the winning objective owner has no slot, so no roster or name exists.
                    winnerIds = new[] { teamOfPlayer.First(kv => kv.Value == winningTeam).Key };
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