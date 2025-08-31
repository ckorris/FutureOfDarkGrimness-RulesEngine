
namespace FDG.Stages
{

    public class StartOfRoundExtraActionStage : StageBase<IMainPhaseContext>
    {
        public StageBinding OnFinished;
        public StartOfRoundExtraActionStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            //TODO: Implement.
            context.Log($"Entered {nameof(StartOfRoundExtraActionStage)}.");
            OnFinished.Activate(context);
        }
    }
}