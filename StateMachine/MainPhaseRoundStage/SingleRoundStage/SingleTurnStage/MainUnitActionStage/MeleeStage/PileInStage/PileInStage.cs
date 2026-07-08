
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
            GameContext.LogDebug("Entered pile in stage.");

            var chargingUnit = context.AttackingUnit.GetValue();
            var defendingUnit = context.DefendingUnit.GetValue();

            // #159: every enemy of the defender OTHER than the charging unit — third parties, or a unit it is
            // already engaged with — is a hard obstacle the defender must not pile through on its way to the
            // charger. Without this a defender plowed straight into a different enemy's base (deeply overlapping).
            var otherEnemies = MovementUtilities.GetEnemyModelFootprints(
                context.DefendingUnit, GameContext, excludeUnit: context.AttackingUnit);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingUnit.ModelBindings,
                defendingUnit.ModelBindings,
                GameContext.TableState.Terrain.Objects,
                otherEnemies);

            foreach (var move in moves)
            {
                move.Model.GetValue().SetPosition(move.NewPosition);
            }

            if (moves.Count > 0)
            {
                GameContext.Log($"Pile in: {moves.Count} defender model(s) moved toward the charging unit.");
            }

            await OnPiledIn.Activate(context);
        }
    }
}
