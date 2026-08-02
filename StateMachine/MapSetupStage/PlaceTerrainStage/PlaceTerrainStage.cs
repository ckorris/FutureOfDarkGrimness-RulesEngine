using System.Linq;
using FDG.Presentation.Beats;
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
    ///   <item><c>Alternating</c> ("Alternating: One Per"): loops <see cref="GameSettings.TerrainPieceCount"/>
    ///     times, alternating teams in the order chosen by <see cref="RollForFirstTerrainPlacementStage"/>,
    ///     asking each active player to place one piece via <see cref="PlaceOneTerrainRequest"/>.</item>
    ///   <item><c>AlternatingPoints</c> ("Alternating: Points", #301): same alternation, but pieces cost
    ///     <see cref="TerrainPieceEntry.Points"/> and each turn spends
    ///     <see cref="GameSettings.TerrainPointsPerTurn"/> from the player's pre-dealt share of
    ///     <see cref="GameSettings.TerrainPointsTotal"/> (see <see cref="TerrainPointsLedger"/>).</item>
    /// </list>
    /// </summary>
    public class PlaceTerrainStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnTerrainPlaced;

        /// <summary>Inclusive upper bound on the Alternating-mode piece count, per #002 Decisions.</summary>
        public const int MaxAlternatingPieceCount = 30;

        /// <summary>Inclusive upper bound on the Alternating: Points total (#301). 60 points of 1-cost fences is ~60 pieces - well past any sane board.</summary>
        public const int MaxPointsTotal = 60;

        /// <summary>Inclusive upper bound on the Alternating: Points per-turn spend (#301).</summary>
        public const int MaxPointsPerTurn = 6;

        /// <summary>
        /// True if the terrain phase should be skipped entirely (no roll, no placement).
        /// Triggers only when an alternating mode is paired with a count/total of 0;
        /// AutoFromLayout / LoadFromFile have their own pool/file that already may be empty.
        /// </summary>
        public static bool ShouldSkipTerrainPhase(GameSettings settings) => settings.TerrainPlacementMode switch
        {
            ETerrainPlacementMode.Alternating => settings.TerrainPieceCount <= 0,
            ETerrainPlacementMode.AlternatingPoints => settings.TerrainPointsTotal <= 0,
            _ => false,
        };

        /// <summary>
        /// True only when players actually take turns placing terrain — an alternating mode with a positive
        /// piece count / point total, the cases the terrain roll-off's alternation order is used. Automatic
        /// modes (AutoFromLayout / LoadFromFile) place terrain without player turns, so the roll-off ("who
        /// places terrain first") is meaningless and is skipped, going straight to the objective phase.
        /// </summary>
        public static bool NeedsTerrainRollOff(GameSettings settings) => settings.TerrainPlacementMode switch
        {
            ETerrainPlacementMode.Alternating => settings.TerrainPieceCount > 0,
            ETerrainPlacementMode.AlternatingPoints => settings.TerrainPointsTotal > 0,
            _ => false,
        };

        public PlaceTerrainStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnTerrainPlaced = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.LogDebug($"Entered {nameof(PlaceTerrainStage)} in mode {context.GameContext.Settings.TerrainPlacementMode}.");

            if (ShouldSkipTerrainPhase(context.GameContext.Settings))
            {
                context.Log("  Terrain count / point total is 0; skipping terrain placement.");
                await OnTerrainPlaced.Activate(context);
                return;
            }

            switch (context.GameContext.Settings.TerrainPlacementMode)
            {
                case ETerrainPlacementMode.AutoFromLayout:
                    PlaceAutoLayout(context, DefaultTerrainPool.Get());
                    break;

                case ETerrainPlacementMode.LoadFromFile:
                    PlaceFromUserFile(context);
                    break;

                case ETerrainPlacementMode.Alternating:
                    await RunAlternatingPlacement(context);
                    break;

                case ETerrainPlacementMode.AlternatingPoints:
                    await RunPointsPlacement(context);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(ETerrainPlacementMode)}: {context.GameContext.Settings.TerrainPlacementMode}.");
            }

            await OnTerrainPlaced.Activate(context);
        }

        private static void PlacePiecesVerbatim(IMapSetupContext context, TerrainLayoutFile layout)
        {
            foreach (TerrainPieceEntry entry in layout.Pieces)
            {
                if (entry.Shape == null) continue;
                context.GameContext.GameDataStore.Create(
                    new TerrainData(entry.TerrainType, entry.Shape, entry.HeightInches, entry.Name));
            }
        }

        /// <summary> Probability that a default-layout piece sitting in a deployment zone is actually placed. </summary>
        private const float DeploymentZonePlacementChance = 0.4f;

        /// <summary>Max distance (inches) a midfield auto-layout piece may drift from its authored center.</summary>
        public const float MidfieldJitterMaxInches = 10f;

        /// <summary>Max rotation wobble (degrees) applied to a jittered midfield auto-layout piece.</summary>
        public const float MidfieldJitterMaxRotationDegrees = 15f;

        /// <summary>
        /// Jitter attempts per midfield piece. The offset shrinks toward zero across attempts, so the
        /// final attempt is the authored pose itself - collision resolution degrades gracefully toward
        /// the known-good layout instead of giving up at full drift.
        /// </summary>
        private const int MidfieldJitterAttempts = 12;

        /// <summary>
        /// Like <see cref="PlacePiecesVerbatim"/>, but with two sources of seeded variety: a piece whose
        /// centre falls inside a deployment zone is only placed <see cref="DeploymentZonePlacementChance"/>
        /// of the time (so there's still some terrain in the deployment zones, just less often), and every
        /// midfield piece is jittered around its authored pose via <see cref="TryJitterMidfieldPiece"/>.
        /// Deployment-zone pieces are settled first so midfield jitter validates against the whole board.
        /// Only the built-in auto layout gets either treatment; a user's LoadFromFile layout is placed verbatim.
        /// </summary>
        private static void PlaceAutoLayout(IMapSetupContext context, TerrainLayoutFile layout)
        {
            // #198: all randomness here draws from the game's seeded source. This was `new System.Random()` -
            // the last unseeded RNG on the game path (missed by #193's audit because the fully-qualified
            // spelling dodged its grep) - so the auto layout differed every run regardless of the seed,
            // and every seeded game diverged from the terrain onward.
            System.Random rng = context.GameContext.Rng;
            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deploy = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;

            var placed = new List<ITerrain>();
            void Create(TerrainPieceEntry entry, IZone shape)
            {
                var data = new TerrainData(entry.TerrainType, shape, entry.HeightInches, entry.Name);
                placed.Add(data);
                context.GameContext.GameDataStore.Create(data);
            }

            var midfield = new List<TerrainPieceEntry>();
            foreach (TerrainPieceEntry entry in layout.Pieces)
            {
                if (entry.Shape == null) continue;

                if (!IsInDeploymentZone(entry.Shape, tableH, deploy))
                {
                    midfield.Add(entry);
                    continue;
                }

                if (rng.NextDouble() > DeploymentZonePlacementChance)
                {
                    context.Log($"  Auto layout: skipping a deployment-zone piece ({entry.TerrainType}).");
                    continue;
                }

                Create(entry, entry.Shape);
            }

            foreach (TerrainPieceEntry entry in midfield)
            {
                IZone? pose = TryJitterMidfieldPiece(entry.Shape, rng, tableW, tableH, deploy, placed);
                if (pose == null)
                {
                    // Only reachable when an earlier piece jittered onto this piece's authored spot.
                    context.Log($"  Auto layout: no clear spot for a midfield piece ({entry.TerrainType}); skipping it.");
                    continue;
                }

                Create(entry, pose);
            }
        }

        /// <summary>
        /// Picks a seeded random pose for a midfield auto-layout piece: up to
        /// <see cref="MidfieldJitterMaxInches"/> of drift per axis and
        /// <see cref="MidfieldJitterMaxRotationDegrees"/> of rotation wobble around the authored pose.
        /// Collisions resolve by shrinking the jitter toward the authored pose over
        /// <see cref="MidfieldJitterAttempts"/> attempts; a candidate must sit fully on the table, overlap
        /// nothing already placed, and keep its center out of the deployment bands (drifting in would undo
        /// the band thinning). Returns null only when even the authored pose is blocked.
        /// </summary>
        public static IZone? TryJitterMidfieldPiece(
            IZone shape, System.Random rng, float tableW, float tableH, float deploy,
            IReadOnlyList<ITerrain> placed)
        {
            Float2 origin = TerrainTemplateUtilities.GetCenter(shape);

            for (int attempt = 0; attempt <= MidfieldJitterAttempts; attempt++)
            {
                float scale = 1f - attempt / (float)MidfieldJitterAttempts;
                float dx = NextSigned(rng) * MidfieldJitterMaxInches * scale;
                float dz = NextSigned(rng) * MidfieldJitterMaxInches * scale;
                // Rotating a lone circle about its own center is a no-op; skip the wrapper.
                float wobble = shape is CircularZone
                    ? 0f
                    : NextSigned(rng) * MidfieldJitterMaxRotationDegrees * scale;

                float cz = origin.Y + dz;
                if (cz < deploy || cz > tableH - deploy) continue;

                IZone candidate = TerrainTemplateUtilities.TranslateToCenter(
                    TerrainTemplateUtilities.Rotate(shape, wobble),
                    new Float2(origin.X + dx, cz));

                if (TerrainPlacementValidator.Check(candidate, tableW, tableH, placed)
                    == TerrainPlacementValidity.Valid)
                    return candidate;
            }

            return null;
        }

        private static float NextSigned(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        // A piece counts as "in a deployment zone" when its representative centre Z lands within the deploy
        // band of the top (z < deploy) or bottom (z > tableH - deploy) table edge.
        private static bool IsInDeploymentZone(IZone shape, float tableH, float deploy)
        {
            float cz = RepresentativeCenterZ(shape);
            return cz < deploy || cz > tableH - deploy;
        }

        private static float RepresentativeCenterZ(IZone shape)
        {
            if (shape is IBoundedZone bounded) return bounded.Bounds.CenterZ;
            if (shape is CompositeZone composite && composite.Parts.Count > 0)
                return composite.Parts.Average(RepresentativeCenterZ);
            return 0f;
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

            int totalPieces = Math.Clamp(context.GameContext.Settings.TerrainPieceCount, 0, MaxAlternatingPieceCount);
            if (totalPieces == 0) return;
            // #268: the picker offers the full palette (the auto layout's pieces plus the palette-only
            // templates, mostly small impassible objects), not just what AutoFromLayout would place.
            var pool = DefaultTerrainPool.GetPalette();

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
                IZone rotated = TerrainTemplateUtilities.Rotate(template.Shape, result.RotationDegrees);
                IZone placedShape = TerrainTemplateUtilities.TranslateToCenter(rotated, result.Center);

                context.GameContext.GameDataStore.Create(
                    new TerrainData(template.TerrainType, placedShape, template.HeightInches, template.Name));

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
                    taskName: $"Placing Terrain ({piecesPlaced + 1} of {totalPieces})",
                    piecesPlaced: piecesPlaced,
                    totalPieces: totalPieces,
                    pool: pool,
                    tableWidthInches: tableW,
                    tableHeightInches: tableH);

                TerrainPlacementResult result = await context.GameContext.PlayerRequester
                    .RequestDecision<PlaceOneTerrainRequest, TerrainPlacementResult>(request);

                if (result.TemplateIndex < 0 || result.TemplateIndex >= pool.Count)
                {
                    context.LogDebug($"  Resolver returned out-of-range template index {result.TemplateIndex}; re-prompting.");
                    continue;
                }

                IZone rotatedCandidate = TerrainTemplateUtilities.Rotate(
                    pool[result.TemplateIndex].Shape, result.RotationDegrees);
                IZone candidateShape = TerrainTemplateUtilities.TranslateToCenter(rotatedCandidate, result.Center);

                var validity = TerrainPlacementValidator.Check(
                    candidateShape, tableW, tableH,
                    context.GameContext.TableState.Terrain.Objects);

                if (validity == TerrainPlacementValidity.Valid)
                    return result;

                context.LogDebug($"  Resolver returned invalid placement ({validity}); re-prompting.");
            }
        }

        /// <summary>Amber, matching the Shaken-family warning Toasts.</summary>
        private static readonly TextColor PointsNoticeColor = new TextColor(240, 200, 90, 255);

        private async Task RunPointsPlacement(IMapSetupContext context)
        {
            if (context.TerrainPlacementTeamOrder is not IReadOnlyList<ITeam> teamOrder)
                throw new InvalidOperationException(
                    $"{nameof(PlaceTerrainStage)} entered AlternatingPoints mode before {nameof(IMapSetupContext.TerrainPlacementTeamOrder)} was set.");

            int totalPoints = Math.Clamp(context.GameContext.Settings.TerrainPointsTotal, 0, MaxPointsTotal);
            int perTurn = Math.Clamp(context.GameContext.Settings.TerrainPointsPerTurn, 1, MaxPointsPerTurn);
            if (totalPoints == 0) return;

            var pool = DefaultTerrainPool.GetPalette();
            if (pool.Count == 0)
            {
                context.Log("  Default pool is empty; no terrain will be placed.");
                return;
            }

            var ledger = new TerrainPointsLedger(teamOrder, totalPoints, perTurn);
            foreach (ITeam team in teamOrder)
                foreach (PlayerID player in team.Players)
                    context.Log($"  {context.GetPlayerName(player)} will place {ledger.AllotmentOf(player)} of the {totalPoints} terrain points.");

            var cursor = new TeamPlayerAlternationCursor(teamOrder);
            while (true)
            {
                PlayerID placer = cursor.GetCurrentPlayerID();
                if (ledger.HasPointsRemaining(placer))
                    await RunPointsTurn(context, ledger, placer, pool);

                if (!cursor.TryAdvance(ledger.TeamHasPointsRemaining, ledger.HasPointsRemaining, out _, out _))
                    break;
            }
        }

        private async Task RunPointsTurn(IMapSetupContext context, TerrainPointsLedger ledger,
            PlayerID placer, IReadOnlyList<TerrainPieceEntry> pool)
        {
            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            string name = context.GetPlayerName(placer);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(placer);
            if (turn.BudgetRemaining <= 0)
            {
                // Deep debt from an earlier over-budget piece consumed the whole turn.
                context.Log($"  {name}'s terrain turn is skipped ({turn.DebtPaidThisTurn} points of debt paid).");
                await context.Announce(
                    $"{name}'s terrain turn is skipped - debt from an earlier piece used its points",
                    PointsNoticeColor, EBannerTier.Toast);
                return;
            }

            while (turn.BudgetRemaining > 0)
            {
                if (!AnyPlayableTemplateFits(turn.Snapshot(), pool, tableW, tableH, context))
                {
                    int forfeited = ledger.RemainingOf(placer);
                    ledger.ForfeitRemaining(placer);
                    context.Log($"  No affordable terrain piece fits anywhere; {name} forfeits {forfeited} points.");
                    await context.Announce(
                        $"No affordable terrain piece fits on the table - {name} forfeits {TerrainPointsBudget.Pts(forfeited)}",
                        PointsNoticeColor, EBannerTier.Toast);
                    return;
                }

                TerrainPlacementResult result = await RequestPointsPlacementWithValidation(
                    context, placer, turn, pool, tableW, tableH);

                TerrainPieceEntry template = pool[result.TemplateIndex];
                int cost = TerrainPointsBudget.CostOf(template);
                IZone rotated = TerrainTemplateUtilities.Rotate(template.Shape, result.RotationDegrees);
                IZone placedShape = TerrainTemplateUtilities.TranslateToCenter(rotated, result.Center);

                context.GameContext.GameDataStore.Create(
                    new TerrainData(template.TerrainType, placedShape, template.HeightInches, template.Name));

                turn.RecordPlacement(cost);
                context.Log($"  {name} placed {template.Name} for {TerrainPointsBudget.Pts(cost)} " +
                    $"({ledger.RemainingOf(placer)} of {ledger.AllotmentOf(placer)} left, debt {ledger.DebtOf(placer)}).");
            }

            if (!ledger.HasPointsRemaining(placer))
            {
                await context.Announce($"{name} has placed all {ledger.AllotmentOf(placer)} of their terrain points",
                    PointsNoticeColor, EBannerTier.Toast);
            }
        }

        private async Task<TerrainPlacementResult> RequestPointsPlacementWithValidation(
            IMapSetupContext context, PlayerID placer, TerrainPointsLedger.Turn turn,
            IReadOnlyList<TerrainPieceEntry> pool, float tableW, float tableH)
        {
            while (true)
            {
                TerrainPointsBudget budget = turn.Snapshot();
                var request = new PlaceOneTerrainRequest(
                    targetPlayerID: placer,
                    taskName: $"Placing Terrain ({budget.AllotmentRemaining} of {budget.AllotmentTotal} points left)",
                    piecesPlaced: 0,
                    totalPieces: 0,
                    pool: pool,
                    tableWidthInches: tableW,
                    tableHeightInches: tableH,
                    pointsBudget: budget);

                TerrainPlacementResult result = await context.GameContext.PlayerRequester
                    .RequestDecision<PlaceOneTerrainRequest, TerrainPlacementResult>(request);

                if (result.TemplateIndex < 0 || result.TemplateIndex >= pool.Count)
                {
                    context.LogDebug($"  Resolver returned out-of-range template index {result.TemplateIndex}; re-prompting.");
                    continue;
                }

                // The budget check is authoritative here - the resolvers' graying is advisory.
                TerrainPieceAffordability verdict = budget.Evaluate(TerrainPointsBudget.CostOf(pool[result.TemplateIndex]));
                if (!verdict.Playable)
                {
                    context.LogDebug($"  Resolver picked an unaffordable template ({verdict.BlockedReason}); re-prompting.");
                    continue;
                }

                IZone rotatedCandidate = TerrainTemplateUtilities.Rotate(
                    pool[result.TemplateIndex].Shape, result.RotationDegrees);
                IZone candidateShape = TerrainTemplateUtilities.TranslateToCenter(rotatedCandidate, result.Center);

                var validity = TerrainPlacementValidator.Check(
                    candidateShape, tableW, tableH,
                    context.GameContext.TableState.Terrain.Objects);

                if (validity == TerrainPlacementValidity.Valid)
                    return result;

                context.LogDebug($"  Resolver returned invalid placement ({validity}); re-prompting.");
            }
        }

        /// <summary>
        /// #301 safety valve: whether ANY currently-playable template has a legal spot (2" grid, 0/90
        /// degree rotations - deliberately conservative; a piece that only fits at 45 degrees on a
        /// near-full table forfeits a little early rather than re-prompting forever). Cheap in normal
        /// play: the scan early-outs at the first legal cell.
        /// </summary>
        private static bool AnyPlayableTemplateFits(TerrainPointsBudget budget,
            IReadOnlyList<TerrainPieceEntry> pool, float tableW, float tableH, IMapSetupContext context)
        {
            const float StepInches = 2f;
            var existing = context.GameContext.TableState.Terrain.Objects;

            foreach (TerrainPieceEntry entry in pool)
            {
                if (!budget.Evaluate(TerrainPointsBudget.CostOf(entry)).Playable) continue;

                foreach (float rotation in new[] { 0f, 90f })
                {
                    IZone rotated = TerrainTemplateUtilities.Rotate(entry.Shape, rotation);
                    (float lx, float hx, float ly, float hy) = rotated.GetAABB();
                    float halfW = (hx - lx) * 0.5f;
                    float halfH = (hy - ly) * 0.5f;

                    for (float x = halfW; x <= tableW - halfW; x += StepInches)
                    {
                        for (float y = halfH; y <= tableH - halfH; y += StepInches)
                        {
                            IZone candidate = TerrainTemplateUtilities.TranslateToCenter(rotated, new Float2(x, y));
                            if (TerrainPlacementValidator.Check(candidate, tableW, tableH, existing)
                                == TerrainPlacementValidity.Valid)
                                return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
