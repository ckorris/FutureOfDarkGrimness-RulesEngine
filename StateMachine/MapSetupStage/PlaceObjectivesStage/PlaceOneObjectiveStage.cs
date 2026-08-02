using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class PlaceOneObjectiveStage : StageBase<IObjectivePlacementTurnContext>
    {
        public StageBinding OnObjectivePlaced;

        private const float MinObjectiveSeparationInches = 9f;

        public PlaceOneObjectiveStage(IGameContext gameContext,
            IStateMachineLayer<IObjectivePlacementTurnContext> parent)
            : base(gameContext, parent)
        {
            OnObjectivePlaced = new StageBinding(this);
        }

        public override async Task Enter(IObjectivePlacementTurnContext context)
        {
            int markerNumber = context.MarkersPlaced + 1;
            context.Log($"Placing objective {markerNumber} of {context.TotalMarkers}.");

            Position placement;
            if (context.GameContext.Settings.ObjectivePlacementMode == EObjectivePlacementMode.AutoPlaced)
            {
                if (!TryAutoPlace(context, out placement))
                {
                    context.Log($"  Auto-placer could not find a legal spot for objective {markerNumber}; skipping.");
                    context.MarkersPlaced = context.TotalMarkers; // stop the loop rather than spin forever
                    await OnObjectivePlaced.Activate(context);
                    return;
                }
            }
            else
            {
                placement = await RequestPlacementFromPlayer(context, markerNumber);
            }

            context.GameContext.GameDataStore.Create(
                new ObjectiveData(placement, context.GameContext.GameDataStore));
            context.MarkersPlaced++;

            context.Log($"  Placed objective {markerNumber} at ({placement.x:F1}, {placement.z:F1}).");
            await OnObjectivePlaced.Activate(context);
        }

        private async Task<Position> RequestPlacementFromPlayer(IObjectivePlacementTurnContext context, int markerNumber)
        {
            var placer = context.GetCurrentPlacingPlayerID();
            var existingObjectives = context.GameContext.TableState.Objectives.Objects.ToList();
            var impassable = GetImpassableTerrain(context).ToList();

            while (true)
            {
                var request = new PlaceObjectiveRequest(
                    targetPlayerID: placer,
                    taskName: $"Placing Objective {markerNumber} of {context.TotalMarkers}",
                    markerIndex: markerNumber,
                    totalMarkers: context.TotalMarkers,
                    legalBand: context.LegalBand,
                    minSeparationInches: MinObjectiveSeparationInches);

                Position candidate = await context.GameContext.PlayerRequester
                    .RequestDecision<PlaceObjectiveRequest, Position>(request);

                var validity = ObjectivePlacementValidator.Check(
                    candidate, context.LegalBand, MinObjectiveSeparationInches,
                    existingObjectives, impassable);

                if (validity == ObjectivePlacementValidity.Valid)
                    return candidate;

                context.Log($"  Player returned invalid placement ({validity}); re-prompting.");
            }
        }

        // Auto-Placed mode: pick every marker via the shared balanced placer (the same algorithm the
        // solo-rules AI uses). Draws from the game's seeded source, so a seeded run reproduces its
        // layout exactly (#193) while an unseeded one still varies between games.
        private bool TryAutoPlace(IObjectivePlacementTurnContext context, out Position placement)
        {
            var existing = context.GameContext.TableState.Objectives.Objects.ToList();
            var impassable = GetImpassableTerrain(context).ToList();
            int markerNumber = context.MarkersPlaced + 1;

            return ObjectiveAutoPlacer.TryChoosePlacement(
                context.LegalBand, MinObjectiveSeparationInches,
                markerNumber, context.TotalMarkers,
                existing, impassable, context.GameContext.Rng, out placement);
        }

        private static IEnumerable<ITerrain> GetImpassableTerrain(IObjectivePlacementTurnContext context) =>
            context.GameContext.TableState.Terrain.Objects.Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible));
    }
}
