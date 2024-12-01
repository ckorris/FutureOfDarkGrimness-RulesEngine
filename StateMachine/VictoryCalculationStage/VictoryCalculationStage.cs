
namespace FDG.Stages
{

    public class VictoryCalculationStage : StageBase<IGameContext>
    {
        public VictoryCalculationStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
        }

        public override void Enter(IGameContext context)
        {
            //Overly simple for now.
            GameContext.TextOutput.Log("Reached victory calculation.");
        }

        public override void Exit()
        {
        }
    }
}