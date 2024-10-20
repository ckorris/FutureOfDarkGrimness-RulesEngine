
namespace FDG.Stages
{

    public class AssignWoundsStage : CombatStage<AssignWoundsResults, AssignWoundsStage, ICombatMetadata>
    {
        public AssignWoundsStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<AssignWoundsResults> onFinished)
        {
            onFinished(new AssignWoundsResults());
        }
    }
}