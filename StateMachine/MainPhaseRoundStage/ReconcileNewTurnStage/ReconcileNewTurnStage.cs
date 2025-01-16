

namespace FDG.Stages
{
    public class ReconcileNewTurnStage : StageBase<IMainPhaseContext>
    {
        public const string TO_START_EXTRA_ACTIONS_TRANSITION = "ReconcileToStartExtraActions";

        public StageBinding ToStartExtraActions;

        public ReconcileNewTurnStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToStartExtraActions = new StageBinding(this);
        }

        public override void Enter(IMainPhaseContext context)
        {
            context.Log($"Entered {nameof(ReconcileNewTurnStage)}.");
            GameContext.GetHandler<IReconcileNewTurnHandler>().Handle(context, ToStartExtraActions.Activate);
        }

    }

    public interface IReconcileNewTurnHandler : IExitOnlyHandler<IMainPhaseContext>
    {

    }
}