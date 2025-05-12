
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

        public override async Task Enter(IMainPhaseContext context)
        {
            //TODO: Implement.
            context.Log($"Entered {nameof(StartOfTurnExtraActionStage)}.");
            ToDetermineFirstTurn.Activate(context);
        }
    }
}