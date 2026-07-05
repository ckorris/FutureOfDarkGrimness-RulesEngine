using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Engine-side implementation of <see cref="IOperationServices"/>: enacts imperative rule
    /// operations against live game state through the existing stage subsystems. Holds the
    /// <see cref="IGameContext"/> so an operation queue (e.g. one produced by accepting an
    /// activated ability) can be run from anywhere via <c>OperationExecutor</c>.
    /// </summary>
    public class GameOperationServices : IOperationServices
    {
        private readonly IGameContext _gameContext;

        public GameOperationServices(IGameContext gameContext)
        {
            _gameContext = gameContext;
        }

        public async Task MoveUnit(IUnit unit, float maxInches, bool isOptional, PlayerID? controller = null)
        {
            DataBinding<UnitData> unitBinding = ResolveUnitBinding(unit);

            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);
            bool ignoresImpassibleTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);

            // The move is directed by the controller (a forced enemy move routes to the caster),
            // falling back to the unit's own owner for the self-move case.
            PlayerID mover = controller ?? unit.PlayerID;

            // A triggered move has a single budget; offer it in every slot so the resolver
            // renders one ring rather than the Advance/Rush/Charge tiers.
            var pathRequest = new DefineMovementPathRequest(mover, "Triggered Move",
                unitBinding, maxInches, maxInches, maxInches,
                WeaponSightProfileBuilder.For(unitBinding.GetValue(), _gameContext.RuleEvaluator),
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain);

            List<ModelMoveEntry> movements = await _gameContext.PlayerRequester
                .RequestDecision<DefineMovementPathRequest, List<ModelMoveEntry>>(pathRequest);

            if (!MovementExecutor.TryMove(_gameContext, unitBinding, movements, maxInches,
                    out List<ReasonForInvalidMove> errors,
                    out MovementExecutor.DangerousTerrainResult dangerResult))
            {
                throw new RequestResponseInvalidException(
                    $"Triggered move for {unit.Name} was invalid: "
                    + string.Join(", ", errors.Select(e => e.ToString())));
            }

            // Present the dangerous-terrain roll, same as the normal-move stage — a triggered move
            // (Vanguard, forced move, etc.) that crosses dangerous terrain shows the roll, not just the wound.
            await MovementExecutor.PresentDangerousTerrainRolls(_gameContext, dangerResult);
        }

        public Task ApplyFatigue(IUnit unit)
        {
            FatigueUtilities.ApplyFatigued(unit);
            return Task.CompletedTask;
        }

        private DataBinding<UnitData> ResolveUnitBinding(IUnit unit)
        {
            foreach (ArmyData army in _gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    if (binding.GetValue().ID.Equals(unit.ID))
                    {
                        return binding;
                    }
                }
            }

            throw new InvalidOperationException(
                $"No data binding found for unit {unit.Name} ({unit.ID}).");
        }
    }
}
