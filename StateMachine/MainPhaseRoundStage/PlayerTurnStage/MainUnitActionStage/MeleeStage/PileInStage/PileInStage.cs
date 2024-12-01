
namespace FDG.Stages
{
    public class PileInStage : StageBase<IMeleeContext>
    {
        public StageBinding OnPiledIn;

        public PileInStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            OnPiledIn = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Entered pile in stage. Skipping for now.");
            OnPiledIn.Activate(context);
        }
    }
}
