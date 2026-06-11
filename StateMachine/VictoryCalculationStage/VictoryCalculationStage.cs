
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
                await context.Announce("It's a tie!");
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
                await context.Announce("It's a tie!");
                GameContext.NotifyGameEnded("It's a tie!");
            }
            else if (winners.Count > 1)
            {
                await context.Announce("It's a tie!");
                GameContext.NotifyGameEnded("It's a tie!");
            }
            else
            {
                var winner = winners[0];
                string winnerName = GameContext.TableState.Players.Objects
                    .FirstOrDefault(p => p.PlayerID == winner)?.Name ?? "A player";
                await context.Announce($"{winnerName} wins!", new TextColor(255, 215, 0, 255));
                GameContext.NotifyGameEnded($"Player {winner.ID} wins!");
            }
        }

        public override void Exit()
        {
        }
    }
}