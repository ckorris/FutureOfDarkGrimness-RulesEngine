
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
            GameContext.TextOutput.Log("Game over — it's a tie!");
            GameContext.NotifyGameEnded("It's a tie!");
        }

        public override void Exit()
        {
        }
    }
}