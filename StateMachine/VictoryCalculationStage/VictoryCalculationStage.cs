
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

            if (objectives.Count == 0)
            {
                GameContext.TextOutput.Log("No objectives on the table - game ends in a tie.");
                GameContext.NotifyGameEnded("It's a tie!");
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
                GameContext.TextOutput.Log("No objectives controlled - game ends in a tie.");
                GameContext.NotifyGameEnded("It's a tie!");
            }
            else if (winners.Count > 1)
            {
                GameContext.TextOutput.Log($"Tied at {topScore} objective(s) each - game ends in a tie.");
                GameContext.NotifyGameEnded("It's a tie!");
            }
            else
            {
                var winner = winners[0];
                GameContext.TextOutput.Log($"Player {winner.ID} wins with {topScore} objective(s)!");
                GameContext.NotifyGameEnded($"Player {winner.ID} wins!");
            }
        }

        public override void Exit()
        {
        }
    }
}