using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Handles string-choice requests for the AI. The main responsibility is Choose Action.
    /// </summary>
    public class AiStringSelectionResolver : IStageResolver<StringSelectionRequest, string>
    {
        private readonly ITableState _tableState;
        private readonly PlayerID _playerID;

        public AiStringSelectionResolver(ITableState tableState, PlayerID playerID)
        {
            _tableState = tableState;
            _playerID = playerID;
        }

        public Task<string> Resolve(StringSelectionRequest request)
        {
            if (request.Instructions == "Choose Action")
                return Task.FromResult(ChooseAction(request.ValidOptions));

            // Pre-attack abilities (#100 #2): the AI doesn't yet reason about when a buff / mark / debuff is
            // worth spending, so it skips them (picks Done) rather than firing one blindly on an arbitrary
            // target. Same conservative stance as the Ambush "always deploy normally" default below; a real
            // policy (buff self / nearest, mark nearest enemy) is a future refinement.
            if (request.ValidOptions.Contains(PreAttackStage.DONE_CHOICE))
                return Task.FromResult(PreAttackStage.DONE_CHOICE);

            // Ambush hold-or-deploy: the AI's reserve placement isn't tactical (it drops the unit at the
            // first legal row from a table edge, ignoring objectives and the enemy), so holding only
            // strands the unit. Always deploy normally instead until reserve placement is smarter.
            if (request.ValidOptions.Contains(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE))
                return Task.FromResult(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);

            // Cast assist / hinder (#103): the AI doesn't yet weigh whether spending its own spell tokens to
            // sway another Caster's roll is worthwhile, so it always declines (spends nothing) rather than
            // burning tokens on an arbitrary cast. Same conservative stance as the pre-attack skip above.
            if (request.ValidOptions.Contains(CastSpellStage.DECLINE_ASSIST_CHOICE))
                return Task.FromResult(CastSpellStage.DECLINE_ASSIST_CHOICE);

            // Fall back to first valid option for any other string selection.
            return Task.FromResult(request.ValidOptions[0]);
        }

        // Priority: Charge > Move (to set up a shoot later) > Shoot (only if enemies in range) > Pass
        private string ChooseAction(IReadOnlyList<string> options)
        {
            if (options.Contains(ChooseActionStage.CHARGE_CHOICE_NAME))
                return ChooseActionStage.CHARGE_CHOICE_NAME;

            if (options.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME))
                return ChooseActionStage.MOVEMENT_CHOICE_NAME;

            if (options.Contains(ChooseActionStage.SHOOT_CHOICE_NAME) && AnyEnemyInShootingRange())
                return ChooseActionStage.SHOOT_CHOICE_NAME;

            return ChooseActionStage.PASS_CHOICE_NAME;
        }

        // Returns true if any living enemy model is within the max ranged weapon range
        // of any of our living models. Does not check LOS — that's the engine's job.
        private bool AnyEnemyInShootingRange()
        {
            var friendlyModels = new List<(Position pos, float maxRange)>();
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID != _playerID) continue;
                foreach (var model in unit.Models)
                {
                    if (model is not ModelData md || !md.GetIsAlive()) continue;
                    float range = md.Weapons
                        .Where(w => w.IsRanged())
                        .Select(w => w.RangeInches)
                        .DefaultIfEmpty(0f)
                        .Max();
                    if (range > 0f)
                        friendlyModels.Add((md.Position, range));
                }
            }

            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == _playerID) continue;
                foreach (var model in unit.Models)
                {
                    if (model is not ModelData md || !md.GetIsAlive()) continue;
                    foreach (var (pos, range) in friendlyModels)
                    {
                        if (Position.GetDistance2D(pos, md.Position) <= range)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
