
namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public StageBinding ToChooseMeleeWeapons;

        public DetermineInRangeDefendersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToChooseMeleeWeapons = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entering Determine In Range Defenders. Skipping, for now we let everyone fight.");
            ToChooseMeleeWeapons.Activate(context);
        }

    }
}
