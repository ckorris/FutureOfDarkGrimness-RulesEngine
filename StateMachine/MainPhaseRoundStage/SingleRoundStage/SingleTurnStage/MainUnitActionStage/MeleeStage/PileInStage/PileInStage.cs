
namespace FDG.Stages
{
    public class PileInStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnPiledIn;

        public PileInStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnPiledIn = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered pile in stage. Skipping for now.");
            OnPiledIn.Activate(context);
        }
    }
}
