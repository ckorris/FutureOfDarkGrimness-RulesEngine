using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Places units set aside during normal deployment (Scout). Runs once, after the normal
    /// deploy loop finishes, for every <see cref="IDeploymentTurnContext.DeferredUnits"/> entry
    /// whose timing is <see cref="EDeferTiming.AfterNormalDeployment"/>: each is placed within a
    /// forward-expanded version of its owner's deployment zone (extended toward table center by the
    /// rule's range), reusing the normal <see cref="PlaceObjectsRequest{T}"/> flow.
    /// <para>#426 (Core Rules v3.6.0): "anywhere fully within 9" of their deployment zone, over 9" away from
    /// non-Scout enemy units". The zone half is the forward extension; the enemy half is keep-out discs
    /// (<see cref="Rules.Dispatch.EnemyKeepOutRules"/>) around every enemy unit that was NOT set aside by this
    /// pass, so an enemy Scout already placed does not push back. Both halves are read from the rule's
    /// <see cref="RuleOperation.DeferDeployment"/>, and the request carries them, so the CLI, GUI and AI
    /// resolvers judge the same numbers.</para>
    /// </summary>
    public class PlaceDeferredUnitsStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        public PlaceDeferredUnitsStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entered Place Deferred Units stage.");

            // Only Scout-style (AfterNormalDeployment) units reach the deferred pool. Ambush units are
            // held interactively at selection time (ChooseUnitToDeployStage) and stay off-table until a
            // later round, so they never appear here.
            foreach (DeferredUnitEntry entry in context.DeferredUnits)
            {
                if (entry.Defer.Timing != EDeferTiming.AfterNormalDeployment) continue;

                UnitData unit = entry.Unit.GetValue();
                PlaceObjectsRequest<ModelData> request = BuildRequest(context, entry);

                // #282: commit-time overlap check, same as normal deployment.
                List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                    .RequestClearPlacement(GameContext, request);

                // On the table is the negation of in reserve (a Scout defers within deployment, so it never
                // carries the token; a legacy save's bootstrap may have stamped it). #309: cleared BEFORE
                // the positions so a networked client never captures an on-table position paired with a
                // still-reserved unit state.
                Rules.Dispatch.ReserveRules.ClearReserve(unit);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }

                context.Log($"{unit.Name} deployed via Scout.");
            }

            await OnFinish.Activate(context);
        }

        /// <summary>The literal TaskName of a Scout placement (the Tactician's discriminator).</summary>
        public const string ScoutTaskName = "Place Scout Unit";

        /// <summary>
        /// The placement request for one set-aside unit: the forward zone, plus - when the rule has an enemy
        /// clause (<see cref="RuleOperation.DeferDeployment.EnemyClearanceInches"/>) - keep-out discs around
        /// every enemy unit that is not itself one of this pass's set-aside units, with the reason worded
        /// from the same number.
        /// </summary>
        public static PlaceObjectsRequest<ModelData> BuildRequest(IDeploymentTurnContext context,
            DeferredUnitEntry entry)
        {
            UnitData unit = entry.Unit.GetValue();
            RectangularZone forwardZone = BuildForwardZone(context, unit, entry.Defer.PlacementRangeInches);

            float clearance = entry.Defer.EnemyClearanceInches;
            var setAside = new HashSet<IUnit>(context.DeferredUnits
                    .Where(other => other.Defer.Timing == EDeferTiming.AfterNormalDeployment)
                    .Select(other => (IUnit)other.Unit.GetValue()),
                ReferenceEqualityComparer.Instance);
            IReadOnlyList<PlacementDisc> keepOut = Rules.Dispatch.EnemyKeepOutRules.Discs(unit,
                context.GameContext.TableState, clearance, setAside.Contains);

            return new PlaceObjectsRequest<ModelData>(unit.PlayerID, ScoutTaskName,
                forwardZone, unit.ModelBindings,
                enemyKeepOutDiscs: keepOut,
                displayName: $"Deploying {unit.Name} (Scout)",
                enemyKeepOutNote: clearance > 0f
                    ? $"Too close to a non-Scout enemy unit - Scout units must be set up over " +
                      $"{clearance:0.##}\" away from them."
                    : "");
        }

        /// <summary>
        /// The owner's deployment zone extended toward the table center (in Z) by
        /// <paramref name="rangeInches"/>, clamped at center so a Scout can't cross midfield. The whole
        /// base must fit inside it (every resolver tests the base footprint), which is the rule's
        /// "fully within".
        /// </summary>
        private static RectangularZone BuildForwardZone(IDeploymentTurnContext context, UnitData unit, float rangeInches)
        {
            ITeam ownerTeam = context.PlayerDeploymentZones!.Keys
                .First(team => team.Players.Contains(unit.PlayerID));
            RectangularZone zone = context.PlayerDeploymentZones[ownerTeam].GetValue();

            float center = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f;
            float zoneCenterZ = (zone.Bottom + zone.Top) / 2f;

            if (zoneCenterZ < center)
            {
                // Bottom zone — extend the inner (Top) edge upward toward center.
                float newTop = Math.Min(zone.Top + rangeInches, center);
                return new RectangularZone(zone.Left, zone.Right, zone.Bottom, newTop);
            }

            // Top zone — extend the inner (Bottom) edge downward toward center.
            float newBottom = Math.Max(zone.Bottom - rangeInches, center);
            return new RectangularZone(zone.Left, zone.Right, newBottom, zone.Top);
        }
    }
}
