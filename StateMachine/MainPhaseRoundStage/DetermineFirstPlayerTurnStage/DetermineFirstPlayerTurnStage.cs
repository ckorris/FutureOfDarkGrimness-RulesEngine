
namespace FDG.Stages
{

    public class DetermineFirstPlayerTurnStage : StageBase<IMainPhaseContext>
    {
        public StageBinding ToPlayerTurn;

        public DetermineFirstPlayerTurnStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToPlayerTurn = new StageBinding(this);
        }

        public override void Enter(IMainPhaseContext context)
        {
            GameContext.Log("Skipping Determine First Player Turn for now.");
            ToPlayerTurn.Activate(context);
        }
    }
}