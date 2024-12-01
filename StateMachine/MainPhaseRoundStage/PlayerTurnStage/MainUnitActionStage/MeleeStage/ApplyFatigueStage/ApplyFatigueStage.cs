
namespace FDG.Stages
{
    public class ApplyFatigueStage : StageBase<IMeleeContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public StageBinding OnFatigueApplied;

        public ApplyFatigueStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            OnFatigueApplied = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Applying fatigue (not really for now)");
            OnFatigueApplied.Activate(context);
        }
    }
}
