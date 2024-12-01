
namespace FDG.Stages
{
    public class AssignMeleeMoralePenaltyStage : StageBase<IMeleeContext>
    {
        public StageBinding OnAssignedPenalty;

        public AssignMeleeMoralePenaltyStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            OnAssignedPenalty = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            //TODO: Finish once we have a way to fatigue a unit.
            GameContext.Log("Assigning melee morale penalty. (Not actually for now)");
            OnAssignedPenalty.Activate(context);
        }
    }
}
