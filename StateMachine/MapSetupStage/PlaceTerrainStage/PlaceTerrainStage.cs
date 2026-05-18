using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// Drives the terrain-setup phase. Behavior switches on
    /// <see cref="GameSettings.TerrainPlacementMode"/>:
    /// <list type="bullet">
    ///   <item><c>AutoFromLayout</c>: dumps the built-in <see cref="DefaultTerrainPool"/> verbatim.</item>
    ///   <item><c>LoadFromFile</c>: dumps <see cref="GameSettings.TerrainLayoutPath"/> verbatim.</item>
    ///   <item><c>Alternating</c>: loops <see cref="GameSettings.TerrainPieceCount"/> times, alternating
    ///     teams in the order chosen by <see cref="RollForFirstTerrainPlacementStage"/>, asking each
    ///     active player to place one piece via <see cref="PlaceOneTerrainRequest"/>.</item>
    /// </list>
    /// </summary>
    public class PlaceTerrainStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnTerrainPlaced;

        /// <summary>Inclusive upper bound on the Alternating-mode piece count, per #002 Decisions.</summary>
        public const int MaxAlternatingPieceCount = 30;

        public PlaceTerrainStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnTerrainPlaced = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.Log($"Entered {nameof(PlaceTerrainStage)} in mode {context.GameContext.Settings.TerrainPlacementMode}.");

            switch (context.GameContext.Settings.TerrainPlacementMode)
            {
                case ETerrainPlacementMode.AutoFromLayout:
                    PlacePiecesVerbatim(context, DefaultTerrainPool.Get());
                    break;

                case ETerrainPlacementMode.LoadFromFile:
                    PlaceFromUserFile(context);
                    break;

                case ETerrainPlacementMode.Alternating:
                    await RunAlternatingPlacement(context);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(ETerrainPlacementMode)}: {context.GameContext.Settings.TerrainPlacementMode}.");
            }

            OnTerrainPlaced.Activate(context);
        }

        private static void PlacePiecesVerbatim(IMapSetupContext context, TerrainLayoutFile layout)
        {
            foreach (TerrainPieceEntry entry in layout.Pieces)
            {
                if (entry.Shape == null) continue;
                context.GameContext.GameDataStore.Create(
                    new TerrainData(entry.TerrainType, entry.Shape, entry.HeightInches));
            }
        }

        private static void PlaceFromUserFile(IMapSetupContext context)
        {
            string? path = context.GameContext.Settings.TerrainLayoutPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                // Lobby launch validation is supposed to block this; log and fall back to empty.
                context.Log($"  {nameof(ETerrainPlacementMode.LoadFromFile)} selected but no path supplied. Skipping terrain placement.");
                return;
            }

            TerrainLayoutFile? layout = TerrainLayoutLoader.TryLoadFromFile(path, out string? error);
            if (layout == null)
            {
                context.Log($"  Failed to load terrain layout from '{path}': {error}. Skipping terrain placement.");
                return;
            }

            PlacePiecesVerbatim(context, layout);
        }

        private async Task RunAlternatingPlacement(IMapSetupContext context)
        {
            if (context.TerrainPlacementTeamOrder is not IReadOnlyList<ITeam> teamOrder)
                throw new InvalidOperationException(
                    $"{nameof(PlaceTerrainStage)} entered Alternating mode before {nameof(IMapSetupContext.TerrainPlacementTeamOrder)} was set.");

            int totalPieces = Math.Clamp(context.GameContext.Settings.TerrainPieceCount, 1, MaxAlternatingPieceCount);
            var pool = DefaultTerrainPool.Get().Pieces;

            if (pool.Count == 0)
            {
                context.Log("  Default pool is empty; no terrain will be placed.");
                return;
            }

            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

            var cursor = new TeamPlayerAlternationCursor(teamOrder);
            int piecesPlaced = 0;

            while (piecesPlaced < totalPieces)
            {
                PlayerID placer = cursor.GetCurrentPlayerID();
                int pieceNumber = piecesPlaced + 1;
                context.Log($"  Placing terrain piece {pieceNumber} of {totalPieces} (player {placer}).");

                TerrainPlacementResult result = await RequestPlacementWithValidation(
                    context, placer, piecesPlaced, totalPieces, pool, tableW, tableH);

                TerrainPieceEntry template = pool[result.TemplateIndex];
                IZone placedShape = TerrainTemplateUtilities.TranslateToCenter(template.Shape, result.Center);

                context.GameContext.GameDataStore.Create(
                    new TerrainData(template.TerrainType, placedShape, template.HeightInches));

                piecesPlaced++;
                cursor.TryAdvance(_ => true, _ => true, out _, out _);
            }
        }

        private async Task<TerrainPlacementResult> RequestPlacementWithValidation(
            IMapSetupContext context, PlayerID placer, int piecesPlaced, int totalPieces,
            IReadOnlyList<TerrainPieceEntry> pool, float tableW, float tableH)
        {
            while (true)
            {
                var request = new PlaceOneTerrainRequest(
                    targetPlayerID: placer,
                    taskName: $"Place terrain piece {piecesPlaced + 1} of {totalPieces}",
                    piecesPlaced: piecesPlaced,
                    totalPieces: totalPieces,
                    pool: pool,
                    tableWidthInches: tableW,
                    tableHeightInches: tableH);

                TerrainPlacementResult result = await context.GameContext.PlayerRequester
                    .RequestDecision<PlaceOneTerrainRequest, TerrainPlacementResult>(request);

                if (result.TemplateIndex < 0 || result.TemplateIndex >= pool.Count)
                {
                    context.Log($"  Resolver returned out-of-range template index {result.TemplateIndex}; re-prompting.");
                    continue;
                }

                IZone candidateShape = TerrainTemplateUtilities.TranslateToCenter(
                    pool[result.TemplateIndex].Shape, result.Center);

                var validity = TerrainPlacementValidator.Check(
                    candidateShape, tableW, tableH,
                    context.GameContext.TableState.Terrain.Objects);

                if (validity == TerrainPlacementValidity.Valid)
                    return result;

                context.Log($"  Resolver returned invalid placement ({validity}); re-prompting.");
            }
        }
    }
}
