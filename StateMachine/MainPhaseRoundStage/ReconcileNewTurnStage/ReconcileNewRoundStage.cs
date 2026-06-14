

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

            // #083 — break the synchronous-continuation chain once per round. With piped/AI input every
            // stage transition completes synchronously, so without a yield the entire game would run as one
            // ever-deepening continuation stack. Yielding at the round boundary caps stack growth.
            await Task.Yield();

            //TODO: Not sure we actually need to do anything here, if we call OnEndOfRound elsewhere.

            await ToStartExtraActions.Activate(context);
        }

    }
}