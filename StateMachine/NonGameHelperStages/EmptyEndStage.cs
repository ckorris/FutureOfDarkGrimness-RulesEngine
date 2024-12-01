
namespace FDG.Stages
{
    public class EmptyEndStage<TContext> : StageBase<TContext>
    {
        public EmptyEndStage(IGameContext gameContext, IStateMachineLayer<TContext> parent) 
            : base(gameContext, parent)
        {
        }

        public override void Enter(TContext context)
        {
            GameContext.Log("End reached.");
        }
    }
}
