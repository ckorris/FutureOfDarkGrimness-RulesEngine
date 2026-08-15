using FDG.Rules.Dispatch;
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
        // #358: set when our own path resolver declined the main move it was just asked for -
        // the movement family must be skipped for the one pick this menu re-offers it.
        private readonly SoloMoveDeclineLatch? _declineLatch;

        public AiStringSelectionResolver(ITableState tableState, PlayerID playerID,
            SoloMoveDeclineLatch? declineLatch = null)
        {
            _tableState = tableState;
            _playerID = playerID;
            _declineLatch = declineLatch;
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

            // #320/#321: a companion action (melee's "hold back this weapon") is an opt-OUT hanging off
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
            // #358: our own path resolver just declined this unit's main move (no legal path -
            // a wedged unit). The engine's Back affordance reopened this menu; picking the
            // movement family again would decline again, forever (the ~1.5M-decision watchdog
            // faults). Skip Charge AND Move for this one pick and end the activation instead.
            bool movementDeclined = _declineLatch?.Consume() == true;

            // #191 A5-10 companion: solo cargo used to ride until the transport died (the gap that
            // justified #335's never-embark). Now that the solo bot loads transports at deployment,
            // it needs a get-out rule: disembark when the ride has ARRIVED. Ranked above everything -
            // an embarked unit's only other real option is Pass, and rule-named options never win a
            // ranked branch by themselves.
            if (options.Contains(CoreRuleCatalog.DisembarkRuleName) && ShouldDisembark())
                return CoreRuleCatalog.DisembarkRuleName;

            if (!movementDeclined && options.Contains(ChooseActionStage.CHARGE_CHOICE_NAME))
                return ChooseActionStage.CHARGE_CHOICE_NAME;

            if (!movementDeclined && options.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME))
                return ChooseActionStage.MOVEMENT_CHOICE_NAME;

            if (options.Contains(ChooseActionStage.SHOOT_CHOICE_NAME) && AnyEnemyInShootingRange())
                return ChooseActionStage.SHOOT_CHOICE_NAME;

            return options.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                ? ChooseActionStage.PASS_CHOICE_NAME
                : FirstActionWorthTaking(options);
        }

        // #335 / #191 A5-10: mid-game embark is the half of #335 that SURVIVED the owner's reversal -
        // "units should very rarely embark into a transport AFTER deployment" (2026-08-15). Deploy-time
        // loading is now taken (AiSelectionResolver) and ShouldDisembark above plans the get-out; a
        // mid-game re-board still has no plan behind it, so it stays filtered.
        // Embark reaches this menu as a rule-NAMED action (ChooseActionStage routes it by
        // offer.RuleName), so the ranked branches above can never return it and only this tail could,
        // by position. Matched on CoreRuleCatalog.EmbarkRuleName the same way the Tactician matches
        // DisembarkRuleName.
        //
        // If Embark is somehow the ONLY option, it is still returned: the fallback MUST stay within
        // ValidOptions or ChooseActionStage faults, and a fault is worse than one unwanted ride.
        private static string FirstActionWorthTaking(IReadOnlyList<string> options)
        {
            foreach (string option in options)
            {
                if (option != CoreRuleCatalog.EmbarkRuleName) return option;
            }
            return options[0];
        }

        // 6" placement radius + roughly one solo move: if the transport is this close to something
        // worth having, the cargo can reach it on foot next activation.
        private const float DisembarkTriggerInches = 12f;

        // Solo-grade arrival test (#191 A5-10, same owner's call as the deploy-time accept in
        // AiSelectionResolver): get out when any friendly LOADED transport stands within
        // DisembarkTriggerInches of an enemy model or an objective we don't already hold. The active
        // unit isn't threaded through Choose Action, so this reads every loaded friendly transport
        // rather than "ours" - exact with one transport (the common case), and with several the worst
        // case is a slightly early hop 6" from the unit's own ride. No live loaded transport at all
        // means the offer is a ghost: get out rather than ride it.
        private bool ShouldDisembark()
        {
            List<IUnit> allUnits = _tableState.Units.Objects.ToList();
            bool anyLoaded = false;
            foreach (IUnit transport in allUnits)
            {
                if (transport.PlayerID != _playerID) continue;
                if (!transport.GetIsOnBattlefield()) continue;
                if (!TransportUtilities.GetOccupants(transport, allUnits).Any()) continue;
                anyLoaded = true;

                foreach (IModel model in transport.Models)
                {
                    if (model is not ModelData md || !md.GetIsAlive()) continue;

                    foreach (IObjective objective in _tableState.Objectives.Objects)
                    {
                        // #296-style team awareness: an objective an ally already holds is no
                        // reason to jump out of the boat.
                        if (objective.OwnerID is PlayerID owner
                            && ITeamExtensions.AreAllied(_tableState.Teams.Objects, _playerID, owner))
                            continue;
                        if (Position.GetDistance2D(md.Position, objective.Position) <= DisembarkTriggerInches)
                            return true;
                    }

                    foreach (IUnit enemy in allUnits)
                    {
                        if (ITeamExtensions.AreAllied(_tableState.Teams.Objects, _playerID, enemy.PlayerID))
                            continue;
                        foreach (IModel enemyModel in enemy.Models)
                        {
                            if (enemyModel is not ModelData emd || !emd.GetIsAlive()) continue;
                            if (Position.GetDistance2D(md.Position, emd.Position) <= DisembarkTriggerInches)
                                return true;
                        }
                    }
                }
            }
            return !anyLoaded;
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
