
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

            int topScore = scoreByPlayer.Values.DefaultIfEmpty(0).Max();
            var winners = scoreByPlayer.Where(kv => kv.Value == topScore).Select(kv => kv.Key).ToList();

            foreach (var kv in scoreByPlayer.OrderByDescending(kv => kv.Value))
                GameContext.TextOutput.Log($"  Player {kv.Key.ID}: {kv.Value} objective(s)");

            if (winners.Count == 0 || topScore == 0)
            {
                await AnnounceTie(context, finalScores, roundsPlayed);
            }
            else if (winners.Count > 1)
            {
                await AnnounceTie(context, finalScores, roundsPlayed);
            }
            else
            {
                var winner = winners[0];
                string winnerName = GameContext.TableState.Players.Objects
                    .FirstOrDefault(p => p.PlayerID == winner)?.Name ?? "A player";
                GameResult result = GameResult.ForWin(winner, winnerName, finalScores, roundsPlayed);
                await context.Announce(result.Message, new TextColor(255, 215, 0, 255));
                GameContext.NotifyGameCompleted(result);
            }
        }

        private async Task AnnounceTie(IGameContext context, IReadOnlyList<PlayerObjectiveScore> finalScores,
            int roundsPlayed)
        {
            GameResult result = GameResult.ForTie(finalScores, roundsPlayed);
            await context.Announce(result.Message);
            GameContext.NotifyGameCompleted(result);
        }

        public override void Exit()
        {
        }
    }
}