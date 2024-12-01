
namespace FDG.Stages
{

    public class ResolveRangedMoraleStage : StageBase<IRangedContext>
    {
        public const string RESOLVE_RANGED_MORALE_FINISHED_TRANSITION =
            "ResolveRangedMoraleFinished";

        public StageBinding ToFinished;

        public ResolveRangedMoraleStage(IGameContext gameContext, IStateMachineLayer<IRangedContext> parent) : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override void Enter(IRangedContext context)
        {
            GameContext.Log("Resolving ranged morale.");
            ToFinished.Activate(context);
        }
    }
}