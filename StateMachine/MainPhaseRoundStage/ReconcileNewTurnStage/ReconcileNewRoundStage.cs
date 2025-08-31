

namespace FDG.Stages
{
    public class ReconcileNewRoundStage : StageBase<IMainPhaseContext>
    {
        public const string TO_START_EXTRA_ACTIONS_TRANSITION = "ReconcileToStartExtraActions";

        public StageBinding ToStartExtraActions;

        public ReconcileNewRoundStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToStartExtraActions = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            context.Log($"Entered {nameof(ReconcileNewRoundStage)}.");

            //TODO: Not sure we actually need to do anything here, if we call OnEndOfRound elsewhere.

            ToStartExtraActions.Activate(context);
        }

    }
}