
namespace FDG.Stages
{

    public class ArmySetupStage : StateBase<ITopLevelContext>
    {
        public const string TO_MAP_SETUP_TRANSITION = "ArmySetupToMapSetup";

        public ArmySetupStage(StateMachine stateMachine, ITopLevelContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.ArmySetupHandler.Handle(Context, MoveToMapSetup);
        }

        private void MoveToMapSetup()
        {
            SignalEvent(TO_MAP_SETUP_TRANSITION);
        }
    }

    public interface IArmySetupHandler : IExitOnlyHandler<ITopLevelContext>
    {

    }
}