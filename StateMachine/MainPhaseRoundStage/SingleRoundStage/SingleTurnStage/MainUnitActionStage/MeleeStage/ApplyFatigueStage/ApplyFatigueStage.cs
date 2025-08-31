
namespace FDG.Stages
{
    public class ApplyFatigueStage : StageBase<ICombatActionContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public StageBinding OnFatigueApplied;

        public ApplyFatigueStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnFatigueApplied = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Applying fatigue (not really for now)");
            OnFatigueApplied.Activate(context);
        }
    }
}
