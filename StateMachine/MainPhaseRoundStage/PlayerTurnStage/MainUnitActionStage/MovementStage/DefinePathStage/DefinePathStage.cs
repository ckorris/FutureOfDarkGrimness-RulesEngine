
namespace FDG.Stages
{
    public class DefinePathStage : StageBase<IMovementActionContext>
    {
        public StageBinding OnPathDefined;

        public DefinePathStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
            OnPathDefined = new StageBinding(this);
        }

        public override void Enter(IMovementActionContext context)
        {
            //TODO: Expand a lot.

            GameContext.GetHandler<IDefinePathHandler>().Handle();
        }
    }

    public interface IDefinePathHandler
    {
        public void Handle(); //TODO: Will need a lot more info.
    }
}
