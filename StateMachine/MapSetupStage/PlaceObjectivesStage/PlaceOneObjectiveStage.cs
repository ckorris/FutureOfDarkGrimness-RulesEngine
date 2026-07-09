using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class PlaceOneObjectiveStage : StageBase<IObjectivePlacementTurnContext>
    {
        public StageBinding OnObjectivePlaced;

        // Used by the debug auto-placer. Matches the previous PlaceObjectivesStage.
        private const float MinObjectiveSeparationInches = 9f;
        private const float CandidateGridStepInches = 3f;

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
            if (context.GameContext.Settings.AutoPlaceObjectivesDebug)
            {
                if (!TryAutoPlace(context, out placement))
                {
                    context.Log($"  Debug auto-placer could not find a legal spot for objective {markerNumber}; skipping.");
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
                    taskName: $"Place objective {markerNumber} of {context.TotalMarkers}",
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

        private bool TryAutoPlace(IObjectivePlacementTurnContext context, out Position placement)
        {
            var band = context.LegalBand;
            var existing = context.GameContext.TableState.Objectives.Objects.ToList();
            var impassable = GetImpassableTerrain(context).ToList();

            var candidates = new List<Float2>();
            for (float x = band.Left; x <= band.Right; x += CandidateGridStepInches)
                for (float z = band.Bottom; z <= band.Top; z += CandidateGridStepInches)
                    candidates.Add(new Float2(x, z));

            // Shuffle so auto-placement varies between games. Draws from the game's seeded source, so a
            // seeded run reproduces its layout exactly (#193) while an unseeded one still varies.
            Random rng = context.GameContext.Rng;
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            foreach (var c in candidates)
            {
                var pos = new Position(c.X, c.Y);
                var validity = ObjectivePlacementValidator.Check(
                    pos, band, MinObjectiveSeparationInches, existing, impassable);
                if (validity == ObjectivePlacementValidity.Valid)
                {
                    placement = pos;
                    return true;
                }
            }

            placement = default;
            return false;
        }

        private static IEnumerable<ITerrain> GetImpassableTerrain(IObjectivePlacementTurnContext context) =>
            context.GameContext.TableState.Terrain.Objects.Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible));
    }
}
