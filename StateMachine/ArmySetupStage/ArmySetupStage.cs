
namespace FDG.Stages
{

    public class ArmySetupStage : StageBase<IGameContext>
    {
        public StageBinding ToMapSetup;

        public ArmySetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMapSetup = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            GameContext.GetHandler<IArmySetupHandler>().Handle(GameContext, ToMapSetup.Activate);
        }
    }

    public interface IArmySetupHandler : IExitOnlyHandler<IGameContext>
    {

    }
}