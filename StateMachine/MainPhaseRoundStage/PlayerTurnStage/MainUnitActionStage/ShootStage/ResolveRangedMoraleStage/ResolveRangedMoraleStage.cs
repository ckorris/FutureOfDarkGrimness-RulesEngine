
namespace FDG.Stages
{

    public class ResolveRangedMoraleStage : StageBase<ICombatActionContext>
    {
        public const string RESOLVE_RANGED_MORALE_FINISHED_TRANSITION =
            "ResolveRangedMoraleFinished";

        public StageBinding ToFinished;

        public ResolveRangedMoraleStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Resolving ranged morale.");
            ToFinished.Activate(context);
        }
    }
}