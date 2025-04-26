
namespace FDG.Stages
{
    public class AssignMeleeMoralePenaltyStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnAssignedPenalty;

        public AssignMeleeMoralePenaltyStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnAssignedPenalty = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            //TODO: Finish once we have a way to fatigue a unit.
            GameContext.Log("Assigning melee morale penalty. (Not actually for now)");
            OnAssignedPenalty.Activate(context);
        }
    }
}
