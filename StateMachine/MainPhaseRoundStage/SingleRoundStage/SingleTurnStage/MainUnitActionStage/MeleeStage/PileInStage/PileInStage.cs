
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
            GameContext.Log("Entered pile in stage.");

            var chargingUnit = context.AttackingUnit.GetValue();
            var defendingUnit = context.DefendingUnit.GetValue();

            var moves = PileInUtilities.ComputePileInMoves(
                chargingUnit.ModelBindings,
                defendingUnit.ModelBindings,
                GameContext.TableState.Terrain.Objects);

            foreach (var move in moves)
            {
                move.Model.GetValue().SetPosition(move.NewPosition);
            }

            if (moves.Count > 0)
            {
                GameContext.Log($"Pile in: {moves.Count} defender model(s) moved toward the charging unit.");
            }

            OnPiledIn.Activate(context);
        }
    }
}
