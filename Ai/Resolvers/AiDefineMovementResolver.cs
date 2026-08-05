using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Moves each AI unit toward the nearest living enemy.
    ///
    /// Melee/Hybrid units rush at full charge distance to close the gap.
    /// Shooting units advance at advance distance (preserving the ability to shoot afterward).
    ///
    /// #191 A3a: the move-construction mechanics (candidate building, gap-targeted refinement, the
    /// validate-with-backoff ladder, footprint gathering) live in <see cref="MovementPlanner"/>, shared
    /// with the Tactician; this resolver keeps only its POLICY - which enemy, what distance, and the
    /// cheap terrain skirting. Behavior is pinned unchanged by this fixture's tests and the benchmark
    /// outcome hashes (see the #191 ledger).
    /// </summary>
    public class AiDefineMovementResolver : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly ITableState _tableState;
        private readonly PlayerID _playerID;
        // #358: armed when this resolver declines the MAIN activation move, so the action menu
        // the decline reopens does not deterministically re-pick Move forever. Null-safe for
        // callers outside a solo set (tests, ad-hoc use) - they just keep the raw #208 decline.
        private readonly SoloMoveDeclineLatch? _declineLatch;

        public AiDefineMovementResolver(ITableState tableState, PlayerID playerID,
            SoloMoveDeclineLatch? declineLatch = null)
        {
            _tableState = tableState;
            _playerID = playerID;
            _declineLatch = declineLatch;
        }

        /// <summary>
        /// The AI answers with a path (standing still is expressed as a zero-length one, not a cancel) --
        /// EXCEPT when no legal path exists at all (a unit still intermingled with the enemy whose living
        /// models cannot re-pack into cohesion without crossing an enemy base). A cancellable move (an
        /// optional post-combat "may move") is then declined via Cancelled rather than by submitting a
        /// cohesion-breaking path the executor would reject and fault on (#208); a non-cancellable forced
        /// move must still return a path, so it holds exact positions as before.
        /// </summary>
        public async Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            List<ModelMoveEntry>? path = await ChoosePath(request);
            // #358: a declined MAIN move bounces back to the action menu - arm the latch so the
            // menu pick skips the movement family once instead of looping the decline forever.
            // A declined TRIGGERED move (MainActivationMove false) is final and arms nothing.
            if (path == null && request.MainActivationMove) _declineLatch?.Arm();
            return path == null
                ? new Cancelled<List<ModelMoveEntry>>()
                : new Selected<List<ModelMoveEntry>>(path);
        }

        // Null return: no legal path exists and the move is cancellable, so Resolve replies Cancelled (#208).
        private Task<List<ModelMoveEntry>?> ChoosePath(DefineMovementPathRequest request)
        {
            var unitBinding = request.UnitDataBinding;
            var unit = unitBinding.GetValue();
            var archetype = AiUnitClassifier.Classify(unit);

            // Shooting units advance (can still shoot); melee/hybrid rush to close distance.
            float moveDistance = archetype == AiUnitArchetype.Shooting
                ? request.MaxAdvanceDistance - 0.001f
                : request.MaxDistanceInches - 0.001f;

            // #093: validate each model against its OWN budget (a joined hero's Fast/Slow), matching the
            // authoritative stage check. The AI moves the whole unit as a grid, so this mainly backs the step
            // off when a tighter (Slow) model would exceed its budget; a unit with no per-model rules gets the
            // unit scalars for every model.
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };

            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(_tableState, _playerID);
            // #205: friendly models the move may pass through but must not END stacked on - fed to the ladder
            // so the AI backs off a spot that would overlap a teammate (the authoritative stage rejects it).
            var friendlyFootprints = MovementPlanner.LiveFriendlyFootprints(_tableState, _playerID, unit.ID);

            // Move (and re-form) only the living models. Dead ones leave holes in the formation and stay
            // put — they're omitted from the move entries, so the cohesion check only sees living models.
            var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0)
                return Task.FromResult(MovementPlanner.StayInPlace(unitBinding));

            // #256: the stand-still early-outs bypass the ladder, so they must validate the reform
            // themselves - with every enemy dead (objectives keep the game going) a unit parked next
            // to a teammate would otherwise submit a re-pack whose slot lands on the friendly and
            // fault DefinePathStage (bench seed 1051).
            if (enemyFootprints.Count == 0)
                return Task.FromResult<List<ModelMoveEntry>?>(SafeStay());

            List<ModelMoveEntry> SafeStay() => MovementPlanner.StayInPlaceValidated(unitBinding, living,
                budgetFor, enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), friendlyFootprints);

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);

            EnemyModelFootprint nearest = enemyFootprints
                .OrderBy(f => Dist(f.Center.x, f.Center.z, cx, cz))
                .First();

            float dx = nearest.Center.x - cx;
            float dz = nearest.Center.z - cz;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            if (dist < 0.01f)
                return Task.FromResult<List<ModelMoveEntry>?>(SafeStay());

            float ndx = dx / dist;
            float ndz = dz / dist;

            // Aim for an explicit end gap rather than a blind "1" short of centre" (which is actually base
            // overlap). A melee/hybrid unit that can reach charges into base contact; everyone else advances
            // to the standoff line.
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
            float contactDistance = leadRadius + nearest.BaseRadiusInches;
            // #355: an impact-only unit (Impact/Ravage, no melee weapon) closes to contact when it can
            // reach - that IS its melee attack. Its ARCHETYPE stays Shooting on purpose: a tank's guns are
            // its output, and Hybrid would make it rush (forfeiting the shot) across the whole table
            // instead of advancing. So it seeks contact only when contact is already within this move.
            bool wantsCharge = (archetype != AiUnitArchetype.Shooting
                    || Rules.Dispatch.ChargeContactRules.ActsOnChargeContact(unit))
                && (dist - contactDistance) <= moveDistance + 0.05f;
            float targetGap = wantsCharge
                ? MovementPlanner.ChargeContactTargetGapInches
                : GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + MovementPlanner.StandoffTargetMarginInches;

            float initialStep = Math.Clamp(dist - contactDistance - targetGap, 0f, moveDistance);
            float step = MovementPlanner.RefineStepTowardGap(unitBinding, living, cx, cz, ndx, ndz,
                initialStep, moveDistance, targetGap, enemyFootprints, request.MaxDistanceInches);

            // (A) Terrain-aware centroid steering. Inflate terrain footprints by the unit's base radius (the
            // largest living model) so this matches the base-radius-aware path validator (#050).
            var allTerrain = _tableState.Terrain.Objects.ToList();
            float baseRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            // If the straight line to the target crosses impassible terrain, try angling left/right to skirt
            // it (a cheap "go around", not full pathfinding) and take the clear direction that lands closest
            // to the target; otherwise the backoff below shortens the move so the unit at least part-advances.
            // Flying ignores impassible terrain, so don't bother skirting.
            if (!request.IgnoresImpassibleTerrain
                && CentroidCrossesTerrain(allTerrain, ETerrainType.Impassible, cx, cz, ndx, ndz, step, baseRadius)
                && TryFindSkirtingDirection(allTerrain, cx, cz, ndx, ndz, step, baseRadius,
                    nearest.Center, out float skirtDx, out float skirtDz))
            {
                ndx = skirtDx;
                ndz = skirtDz;
            }

            // Strider waives the difficult-terrain cap, so don't pre-clamp the AI's step to it (the path
            // validator below also skips the cap for this unit, so a clamp here would only under-move).
            if (!request.IgnoresDifficultTerrain
                && CentroidCrossesTerrain(allTerrain, ETerrainType.Difficult, cx, cz, ndx, ndz, step, baseRadius))
                step = Math.Min(step, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            // (B) The centroid pre-filter can't see per-model paths, cohesion, enemy-unit crossings, or
            // charge-reach — so run the planner's ladder against the same MovementUtilities.ValidatePaths
            // the stage uses. The AI never submits a move the engine will reject (G3).
            List<ModelMoveEntry> candidate = MovementPlanner.ValidateWithBackoff(
                s => MovementPlanner.BuildCandidate(unitBinding, living, cx, cz, ndx, ndz, s, request.MaxDistanceInches),
                step, unitBinding, living, budgetFor, enemyFootprints,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain,
                allTerrain, friendlyFootprints,
                // #256 S2: side-step the pack rather than halve toward zero when the only fault is landing on a friendly.
                (s, lat) => MovementPlanner.BuildCandidate(unitBinding, living, cx, cz, ndx, ndz, s,
                    request.MaxDistanceInches, lateralOffsetInches: lat));

            // The ladder's last resort (hold exact positions) can bottom out at a cohesion-breaking hold when
            // the living models are already spread >1" apart - a unit intermingled with the enemy after melee
            // that cannot re-pack without a model crossing an enemy base. #159 made DefinePathStage lenient so
            // submitting that hold no longer faults the game; but for a CANCELLABLE move (an optional
            // post-combat "may move") a 0" hold achieves nothing, so we still prefer to DECLINE - returning to
            // the action menu keeps the unit's options open rather than spending the optional move standing
            // still. The STRICT coherency check is deliberate here: it is the "did the ladder produce a real,
            // cohesive move, or only a stuck hold?" test that gates the decline. A non-cancellable (forced)
            // move has no decline channel, so it still submits the least-bad candidate, which the now-lenient
            // stage accepts (see Resolve_NoLegalPath_NonCancellableRequest_StillReturnsAPath).
            if (request.AllowCancel && !MovementUtilities.ValidatePaths(candidate, budgetFor,
                    enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                    request.IgnoresImpassibleTerrain, allTerrain, out _, friendlyFootprints))
            {
                return Task.FromResult<List<ModelMoveEntry>?>(null);
            }

            return Task.FromResult<List<ModelMoveEntry>?>(candidate);
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // Angle offsets (degrees, both sides) tried when the straight path is blocked, widening outward.
        // #264 issue 6: capped at +/-60, was +/-100. Past perpendicular a "skirt" is a retreat, and it
        // was taken at the FULL rush budget - a melee unit walled off from its target lurched 12"
        // sideways-to-backwards, every activation, which is a large part of what the walled-unit
        // reports actually looked like on the table. Anything wider than this needs a real route, not
        // an angle: that is what the Tactician's GridPathfinder is for.
        private static readonly float[] SkirtAngleOffsetsDegrees =
            { 20f, -20f, 40f, -40f, 60f, -60f };

        // True if the unit's centroid path (inflated by its base radius) crosses terrain of the given type.
        private static bool CentroidCrossesTerrain(List<ITerrain> terrain, ETerrainType type,
            float cx, float cz, float ndx, float ndz, float step, float baseRadius)
        {
            var start = new Float2(cx, cz);
            var end = new Float2(cx + ndx * step, cz + ndz * step);
            return terrain.Any(t => t.TerrainType.HasFlag(type)
                && t.Shape.DoesPathIntersectZone(start, end, baseRadius));
        }

        // Tries fixed angle offsets either side of the straight direction; returns the impassible-clear one
        // whose full-step end-point lands closest to the target. False when none of the tried angles is clear.
        private static bool TryFindSkirtingDirection(List<ITerrain> terrain, float cx, float cz,
            float ndx, float ndz, float step, float baseRadius, Position target, out float outDx, out float outDz)
        {
            outDx = ndx;
            outDz = ndz;
            float baseAngle = MathF.Atan2(ndz, ndx);
            float bestDist = float.PositiveInfinity;
            bool found = false;

            foreach (float deg in SkirtAngleOffsetsDegrees)
            {
                float a = baseAngle + deg * (MathF.PI / 180f);
                float dx = MathF.Cos(a), dz = MathF.Sin(a);
                if (CentroidCrossesTerrain(terrain, ETerrainType.Impassible, cx, cz, dx, dz, step, baseRadius))
                    continue;

                float endDist = Dist(cx + dx * step, cz + dz * step, target.x, target.z);
                if (endDist < bestDist)
                {
                    bestDist = endDist;
                    outDx = dx;
                    outDz = dz;
                    found = true;
                }
            }

            return found;
        }
    }
}
