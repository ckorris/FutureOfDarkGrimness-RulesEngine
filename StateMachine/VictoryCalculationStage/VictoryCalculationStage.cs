
namespace FDG.Stages
{

    public class VictoryCalculationStage : StageBase<IGameContext>
    {
        public VictoryCalculationStage(StateMachine stateMachine, IGameContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //Overly simple for now.
            Context.TextOutput.Log("Reached victory calculation.");
        }
    }
}