using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #035 slice C — resolves a Disembark action the player chose in <see cref="ChooseActionStage"/>:
    /// it places the embarked unit's models within 6" of its transport (a player placement request),
    /// un-embarks the unit (clears the <c>EmbarkedIn</c> token, so it becomes a normal on-table unit), and
    /// registers the move as an Advance — the unit may then Shoot but not move again — before looping back
    /// to Choose Action.
    ///
    /// Unlike <see cref="CustomActionStage"/> it does NOT resolve the ability's effect generically (the
    /// Disembark ability's effect is a no-op marker; the movement is enacted here). The transport is found
    /// from the unit's <c>EmbarkedIn</c> token, so this reads serialized state and survives a save/load
    /// resume (#094).
    /// </summary>
    public class DisembarkStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinished;

        public DisembarkStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            UnitData unit = context.ActivatingUnit.GetValue();

            UnitData? transport = FindTransport(unit);
            if (transport == null)
            {
                // Defensive: the action is only offered while embarked, but if the link is somehow gone,
                // return to Choose Action rather than throwing mid-activation.
                GameContext.Log($"{unit.Name} tried to disembark but its transport is gone; returning to Choose Action.");
                await OnFinished.Activate(context);
                return;
            }

            Position transportPosition = RepresentativePosition(transport);

            // The exact "fully within 6\"" circle around the transport. The place resolvers scan its
            // bounding box and reject points outside the true circle (IsPointWithinZone), and clamp to the
            // table / avoid impassible terrain.
            CircularZone zone = new CircularZone(transportPosition.Position2D, TransportUtilities.MaxTransportRangeInches);

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"Disembark {unit.Name}",
                zone, unit.ModelBindings);

            List<PlacedObjectEntry<ModelData>> placements = await GameContext.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, List<PlacedObjectEntry<ModelData>>>(request);

            foreach (PlacedObjectEntry<ModelData> placement in placements)
            {
                placement.Binding.GetValue().SetPosition(placement.Position);
            }

            // No longer aboard: clear the EmbarkedIn token (the unit is now a normal on-table unit, and the
            // Disembark ability stops being offered since its AvailableWhen = TokenPresent(EmbarkedIn)).
            TransportUtilities.Disembark(unit);

            // Disembarking IS the unit's move action (Advance-equivalent): it may still Shoot but not move
            // again. MoveDistance 0 keeps shooting available (GetCanShoot gates on move distance).
            context.RegisterMoveFinished(0f);

            GameContext.Log($"{unit.Name} disembarked {transport.Name}.");
            await context.Announce($"{unit.Name} disembarked {transport.Name}.", new TextColor(120, 200, 255, 255));

            await OnFinished.Activate(context);
        }

        private UnitData? FindTransport(UnitData unit)
        {
            UnitID? transportId = TransportUtilities.GetTransportId(unit);
            if (transportId == null) return null;

            UnitID id = transportId.Value;
            foreach (UnitData candidate in GameContext.GameDataStore.GetAllValues<UnitData>())
            {
                if (candidate.ID == id) return candidate;
            }
            return null;
        }

        private static Position RepresentativePosition(UnitData unit)
        {
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) return model.Position;
            }
            // A destroyed transport still has its last position on its (dead) models.
            return unit.Models.Count > 0 ? unit.Models[0].Position : new Position(0f, 0f);
        }
    }
}
