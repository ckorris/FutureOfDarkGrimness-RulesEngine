
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

            // #205: the defender's FRIENDLY units are hard obstacles too - a pile-in may not stack the defender
            // on top of its own teammates any more than on a third-party enemy. Pile-in stops the mover short of
            // every obstacle footprint, so folding friendlies into the same list keeps it off them.
            var friendlyObstacles = MovementUtilities.GetFriendlyModelFootprints(context.DefendingUnit, GameContext);
            var obstacles = otherEnemies.Concat(friendlyObstacles).ToList();

            var moves = PileInUtilities.ComputePileInMoves(
                chargingUnit.ModelBindings,
                defendingUnit.ModelBindings,
                GameContext.TableState.Terrain.Objects,
                obstacles);

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
