
namespace FDG.Stages
{

    public class ArmySetupStage : StateBase<IGameContext>
    {
        public const string TO_MAP_SETUP_TRANSITION = "ArmySetupToMapSetup";

        public ArmySetupStage(StateMachine stateMachine, IGameContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.GetHandler<IArmySetupHandler>().Handle(Context, MoveToMapSetup);
        }

        private void MoveToMapSetup()
        {
            SignalEvent(TO_MAP_SETUP_TRANSITION);
        }
    }

    public interface IArmySetupHandler : IExitOnlyHandler<IGameContext>
    {

    }
}