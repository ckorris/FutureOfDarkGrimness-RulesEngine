using FDG.Presentation.Beats;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Tokens;
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
            //
            // allowCancel tracks optionality: an optional "may move" (Harassing / Hit & Run and the rest
            // of the post-combat family) can be declined, so the resolver may reply Cancelled and a human
            // gets a Back button. A FORCED move (e.g. a spell pushing an enemy) is not cancellable - the
            // rule has already fired and there is nowhere to return to.
            var pathRequest = new DefineMovementPathRequest(mover, "Triggered Move",
                unitBinding, maxInches, maxInches, maxInches,
                WeaponSightProfileBuilder.For(unitBinding.GetValue(), _gameContext.RuleEvaluator),
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                allowCancel: isOptional);

            CancellableResult<List<ModelMoveEntry>> pathResult = await _gameContext.PlayerRequester
                .RequestDecision<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>(pathRequest);

            if (pathResult is not Selected<List<ModelMoveEntry>> selectedPath)
            {
                // Declined. For an optional move that is a legal "no thanks" - typically a unit still
                // intermingled with the enemy after melee whose living models cannot re-pack into
                // cohesion without crossing an enemy base, so no legal destination exists. The unit
                // simply does not move (the caller sees no position change and keeps its budget).
                if (isOptional)
                {
                    _gameContext.Log($"{unit.Name} declines its triggered move - no legal destination.");
                    return;
                }
                throw new RequestResponseInvalidException(
                    $"Triggered move for {unit.Name} cannot be cancelled - the rule that forced it has already fired.");
            }
            List<ModelMoveEntry> movements = selectedPath.Value;

            if (!MovementExecutor.TryMove(_gameContext, unitBinding, movements, maxInches,
                    out List<ReasonForInvalidMove> errors,
                    out MovementExecutor.DangerousTerrainResult dangerResult))
            {
                // The resolver returned a path the authoritative validator rejects. For an optional move
                // this is the same "no legal destination" case as a cancel - a resolver with no decline
                // channel (the headless CLI auto-play) submits its best invalid guess instead of cancelling.
                // Treat it as a decline: the unit does not move. A forced move still faults, surfacing the bug.
                if (isOptional)
                {
                    _gameContext.Log($"{unit.Name} declines its triggered move - no legal destination.");
                    _gameContext.LogDebug($"Triggered move for {unit.Name} declined - resolver path invalid: "
                        + string.Join(", ", errors.Select(e => e.ToString())) + ".");
                    return;
                }
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

        public async Task ClearTokenOnRoll(IUnit unit, Rules.Foundation.TokenType tokenType, int minRoll)
        {
            // RollDecisiveFace, never Roll(1): the outcome is binary — the unit either sheds the token or it
            // does not — so under the probabilistic roller a histogram would want to remove a FRACTION of a
            // token. RollDecisive commits to one face (and RollDecisiveFace throws if a roller ever stops
            // honouring that), which is the same threshold-on-a-decisive-roll shape every other pass/fail
            // test in the engine uses. Routing it through the context roller also keeps it seed-reproducible.
            int face = _gameContext.DiceRoller.RollDecisiveFace();
            bool cleared = face >= minRoll;
            if (cleared)
            {
                unit.Tokens.RemoveTokens(tokenType);
            }

            _gameContext.Log($"{unit.Name} rolled {face} to shed {tokenType} on a {minRoll}+ - " +
                (cleared ? "recovered." : "no effect."));

            // The beat's histogram is exactly the roll that happened: one die, on the face it showed.
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[face - 1] = 1f;

            await _gameContext.Presenter.Present(DiceRolledBeat.From(new DiceResults(perSide), minRoll,
                _gameContext.Settings.RandomnessType, $"Recover from {tokenType}",
                cleared ? "recovered" : "still " + tokenType));
        }

        public async Task GrantTokenOnRoll(IUnit unit, Rules.Tokens.Token token, int minRoll)
        {
            // Decisive for the same reason as ClearTokenOnRoll above: the marker is either placed or it
            // is not — a histogram would want to place a fraction of one.
            int face = _gameContext.DiceRoller.RollDecisiveFace();
            bool placed = face >= minRoll;
            if (placed)
            {
                unit.Tokens.AddToken(token);
            }

            string label = TokenDefinitionCatalog.Lookup(token.Type).Name;
            _gameContext.Log($"Rolled {face} to place {label} on {unit.Name} ({minRoll}+ needed) - " +
                (placed ? "placed." : "no effect."));

            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[face - 1] = 1f;

            await _gameContext.Presenter.Present(DiceRolledBeat.From(new DiceResults(perSide), minRoll,
                _gameContext.Settings.RandomnessType, $"Place {label}",
                placed ? "marker placed" : "no effect"));
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
