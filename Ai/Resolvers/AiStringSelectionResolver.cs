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

            // Ambush hold-or-deploy: the AI's reserve placement isn't tactical (it drops the unit at the
            // first legal row from a table edge, ignoring objectives and the enemy), so holding only
            // strands the unit. Always deploy normally instead until reserve placement is smarter.
            if (request.ValidOptions.Contains(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE))
                return Task.FromResult(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);

            // #316/#317: a companion action (melee's "hold back this weapon") is an opt-OUT hanging off
            // another option. The AI has no policy for when declining is worth it - and for a player that
            // cannot plan around it, declining is strictly worse than acting - so it never picks one.
            // Explicit rather than relying on companions sorting last: the catch-all below is exactly the
            // trap the Ambush case above documents.
            if (request.SecondaryActions is { Count: > 0 })
            {
                HashSet<string> companions = request.SecondaryActions.Values
                    .Select(secondary => secondary.Option)
                    .ToHashSet(StringComparer.Ordinal);
                List<string> primaryOptions = request.ValidOptions
                    .Where(option => !companions.Contains(option))
                    .ToList();
                if (primaryOptions.Count > 0)
                    return Task.FromResult(primaryOptions[0]);
            }

            // Fall back to first valid option for any other string selection.
            return Task.FromResult(request.ValidOptions[0]);
        }

        // Priority: Charge > Move (to set up a shoot later) > Shoot (only if enemies in range) > Pass.
        // "Before attacking" abilities (buffs / marks / Mend) now appear here as their own named options,
        // but the AI doesn't yet reason about when one is worth spending, so it never prefers them: it picks
        // a known action or Pass, only touching an ability name via the options[0] fallback (when neither an
        // attack nor Pass is offered). A real "buff self / mark nearest" policy is a future refinement.
        // The fallback MUST stay within ValidOptions: returning Pass when it isn't offered -- e.g. the
        // unit rushed and now "must engage", leaving only Cast or a forced action -- faults
        // ChooseActionStage ("Request option was Pass, but that wasn't an option"). ValidOptions is
        // guaranteed non-empty (ChooseActionStage auto-passes when it's empty without sending a request).
        private string ChooseAction(IReadOnlyList<string> options)
        {
            if (options.Contains(ChooseActionStage.CHARGE_CHOICE_NAME))
                return ChooseActionStage.CHARGE_CHOICE_NAME;

            if (options.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME))
                return ChooseActionStage.MOVEMENT_CHOICE_NAME;

            if (options.Contains(ChooseActionStage.SHOOT_CHOICE_NAME) && AnyEnemyInShootingRange())
                return ChooseActionStage.SHOOT_CHOICE_NAME;

            return options.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                ? ChooseActionStage.PASS_CHOICE_NAME
                : options[0];
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
                // #296: team-aware - a 2v2 teammate in range must not read as a shootable enemy.
                if (ITeamExtensions.AreAllied(_tableState.Teams.Objects, _playerID, unit.PlayerID)) continue;
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
