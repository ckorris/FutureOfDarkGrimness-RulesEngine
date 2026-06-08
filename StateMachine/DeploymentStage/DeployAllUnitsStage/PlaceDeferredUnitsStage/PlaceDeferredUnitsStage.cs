using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Places units set aside during normal deployment (Scout). Runs once, after the normal
    /// deploy loop finishes, for every <see cref="IDeploymentTurnContext.DeferredUnits"/> entry
    /// whose timing is <see cref="EDeferTiming.AfterNormalDeployment"/>: each is placed within a
    /// forward-expanded version of its owner's deployment zone (extended toward table center by the
    /// rule's range), reusing the normal <see cref="PlaceObjectsRequest{T}"/> flow.
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
            context.Log("Entered Place Deferred Units stage.");

            foreach (DeferredUnitEntry entry in context.DeferredUnits)
            {
                if (entry.Defer.Timing != EDeferTiming.AfterNormalDeployment) continue; // Ambush (LaterRound) handled elsewhere.

                UnitData unit = entry.Unit.GetValue();
                RectangularZone forwardZone = BuildForwardZone(context, unit, entry.Defer.PlacementRangeInches);
                DataBinding<RectangularZone> zoneBinding = GameContext.GameDataStore
                    .GetDataBinding<RectangularZone>(GameContext.GameDataStore.Create(forwardZone));

                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, "Place Scout Unit",
                    zoneBinding, unit.ModelBindings);

                List<PlacedObjectEntry<ModelData>> placements = await GameContext.PlayerRequester
                    .RequestDecision<PlaceObjectsRequest<ModelData>, List<PlacedObjectEntry<ModelData>>>(request);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                }

                context.Log($"{unit.Name} deployed via Scout.");
            }

            OnFinish.Activate(context);
        }

        /// <summary>
        /// The owner's deployment zone extended toward the table center (in Z) by
        /// <paramref name="rangeInches"/>, clamped at center so a Scout can't cross midfield.
        /// </summary>
        private RectangularZone BuildForwardZone(IDeploymentTurnContext context, UnitData unit, float rangeInches)
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
