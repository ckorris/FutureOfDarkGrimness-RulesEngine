
namespace FDG.Stages
{
    public class DetermineInRangeAttackersStage : StageBase<ICombatActionContext>
    {
        public StageBinding ToDetermineDefenders;

        private const float HORIZONTAL_ATTACK_RANGE_INCHES = 2;
        private const float VERTICAL_ATTACK_RANGE_INCHES = 4;

        public DetermineInRangeAttackersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToDetermineDefenders = new StageBinding(this);
        }

        public override void Enter(ICombatActionContext context)
        {
            GameContext.Log("Entering Determine In Range Attackers. Skipping, for now we let everyone fight.");
            ToDetermineDefenders.Activate(context);
        }
    }
}
