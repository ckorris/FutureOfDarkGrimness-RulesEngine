
namespace FDG.Stages
{

    public class StartOfTurnExtraActionStage : StageBase<IMainPhaseContext>
    {
        public const string TO_DETERMINE_FIRST_TURN_TRANSITION = "StartExtraActionsToDetermineFirstTurn";

        public StageBinding ToDetermineFirstTurn;
        public StartOfTurnExtraActionStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToDetermineFirstTurn = new StageBinding(this);
        }

        public override void Enter(IMainPhaseContext context)
        {
            context.Log($"Entered {nameof(StartOfTurnExtraActionStage)}.");
            GameContext.GetHandler<IStartOfTurnExtraActionsHandler>().Handle(context, ToDetermineFirstTurn.Activate);
        }
    }

    public interface IStartOfTurnExtraActionsHandler : IExitOnlyHandler<IMainPhaseContext>
    {

    }
}