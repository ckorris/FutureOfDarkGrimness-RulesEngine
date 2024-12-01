
namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<IMeleeContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public StageBinding ToChooseMelee;

        public DetermineInRangeDefendersStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            ToChooseMelee = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Entering Determine In Range Defenders. Skipping, for now we let everyone fight.");
            ToChooseMelee.Activate(context);
        }

    }
}
