using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System.Runtime.CompilerServices;

namespace FDG.Stages
{
    public static class MovementUtilities
    {
        public static float GetMaxMoveDistance(List<ModelMoveEntry> moves)
        {
            Dictionary<ModelMoveEntry, float> distances = GetTotalMoveDistances(moves);
            return distances.Values.Max();
        }

        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, maxDistanceInches, terrain: null, out errors);

        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            //This overload is only reached with terrain: null (the no-terrain convenience form), so the terrain
            //checks are no-ops regardless — no Strider/Flying flags to thread here.
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain: false, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain: false, ref errors);
            //No enemy footprints supplied (this overload predates enemy-aware validation): the move-through /
            //standoff check is a no-op here, preserving these callers' existing behavior.
            ValidateMovingThroughEnemyUnits(moves, Array.Empty<EnemyModelFootprint>(), canMoveThroughEnemies: false, ref errors);
            ValidateCoherency(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Enemy-aware validation WITHOUT charge semantics: terrain + move-through / standoff (honoring
        /// <paramref name="canMoveThroughEnemies"/>) + coherency, but no charge-reach requirement. For
        /// consolidation and out-of-band executor moves — they have their own distance cap and never charge.
        /// <paramref name="ignoresDifficultTerrain"/> waives the difficult-terrain move cap (Strider/Flying);
        /// <paramref name="ignoresImpassibleTerrain"/> waives the impassible-terrain block (Flying).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherency(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Back-compat charge overload that assumes the mover may not move through enemies
        /// (<c>canMoveThroughEnemies: false</c>) nor ignore difficult/impassible terrain
        /// (<c>false</c>). Kept so callers/tests that never need those rule-derived flags don't have to thread
        /// them (mirrors the no-enemy convenience overloads above).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, maxRushDistance, maxDistanceInches, enemyFootprints,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain, out errors);

        /// <summary>
        /// Full Move-action validation: paths must stay within the hard cap (Charge distance),
        /// and any path that exceeds the Rush distance requires at least one model to end within
        /// melee range of an enemy model. <paramref name="canMoveThroughEnemies"/> waives the
        /// pass-through block for fly-over units (Strafing); <paramref name="ignoresDifficultTerrain"/>
        /// waives the difficult-terrain move cap (Strider/Flying); <paramref name="ignoresImpassibleTerrain"/>
        /// waives the impassible-terrain block (Flying).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, _ => new ModelMoveBudget(maxRushDistance, maxDistanceInches),
                enemyFootprints, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                terrain, out errors);

        /// <summary>
        /// Per-model form of the full Move-action validation (#093): each model is capped by its OWN
        /// <see cref="ModelMoveBudget"/> (a joined hero's Fast/Slow gives it a different budget than the rest
        /// of the unit) instead of one unit-wide pair of scalars. Coherency still reins a fast model in.
        /// The scalar overload above delegates here with the same budget for every model, so unit-wide
        /// callers are unchanged.
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, move => budgetFor(move).MaxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherency(moves, ref errors);
            ValidateChargeReach(moves, move => budgetFor(move).MaxRushDistance, enemies, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Consolidation-move validation (#159): the cap, terrain, and enemy-crossing checks stay strict, but
        /// the coherency rule is LENIENT. A unit left out of coherency by a mid-unit casualty (its survivors
        /// end up &gt;1" apart) can't always re-form within the tiny 1-3" consolidation cap, so a consolidation
        /// is only rejected for coherency when it makes coherency WORSE than it already was (see
        /// <see cref="ValidateCoherencyNotWorsened"/>). A hold, or any move that pulls the unit together, is
        /// therefore always legal — the unit can never be trapped with no valid consolidation.
        /// </summary>
        public static bool ValidateConsolidationPaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherencyNotWorsened(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Every living enemy model's base footprint (centre + radius), tagged with a per-unit key so the
        /// move-through / standoff check can tell which models belong to the same enemy unit (a charge into
        /// one model of a unit legitimately ends within the 1" standoff of that whole unit). The key is only
        /// stable within the returned list. Enemies are everyone not on the moving unit's team.
        /// </summary>
        public static List<EnemyModelFootprint> GetEnemyModelFootprints(DataBinding<UnitData> movingUnit, IGameContext gameContext)
            => GetEnemyModelFootprints(movingUnit, gameContext, excludeUnit: null);

        /// <summary>
        /// As <see cref="GetEnemyModelFootprints(DataBinding{UnitData}, IGameContext)"/>, but omits every model
        /// of <paramref name="excludeUnit"/>. Pile-in uses this to obstacle-check a defender against every enemy
        /// unit EXCEPT the one it is piling toward — so a defender stops at base contact with a third-party (or
        /// already-engaged) enemy instead of plowing through it (#159).
        /// </summary>
        public static List<EnemyModelFootprint> GetEnemyModelFootprints(DataBinding<UnitData> movingUnit,
            IGameContext gameContext, DataBinding<UnitData>? excludeUnit)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<EnemyModelFootprint> footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (ArmyData enemyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyUnit in enemyArmy.UnitBindings)
                {
                    if (excludeUnit != null && ReferenceEquals(enemyUnit.GetValue(), excludeUnit.GetValue())) continue;
                    // #207: embarked / reserve / off-table units are not obstacles. Their models are
                    // parked at the origin (EmbarkStage), so counting their footprints made (0,0) an
                    // invisible wall that rejected any legal move sweeping near the table corner.
                    if (!enemyUnit.GetValue().GetIsOnBattlefield()) continue;
                    // #029: an Aircraft can't be moved into base contact with — tag its footprints so the
                    // validator never lets a charger end engaged with it.
                    bool uncontactable = Rules.Dispatch.AircraftRules.IsAircraft(enemyUnit.GetValue());
                    bool anyLiving = false;
                    foreach (DataBinding<ModelData> enemyModel in enemyUnit.ModelBindings()
                        .Where(m => m.GetIsAlive()))
                    {
                        ModelData md = enemyModel.GetValue();
                        footprints.Add(new EnemyModelFootprint(md.PositionBinding.GetValue(), md.BaseRadiusInches,
                            unitKey, uncontactable, md.BaseShape, md.Facing));
                        anyLiving = true;
                    }
                    if (anyLiving) unitKey++;
                }
            }

            return footprints;
        }

        /// <summary>
        /// The distinct living enemy units whose footprint the move <paramref name="moves"/> pass through:
        /// any moving model's path segment comes within base-to-base contact (moving radius + enemy radius)
        /// of any living enemy model. Trigger detection for Strafing's mid-move attack. Call while the moving
        /// models are still at their START positions (the segment origin is read from each model's current
        /// position), i.e. before the move is committed.
        /// </summary>
        public static List<DataBinding<UnitData>> GetEnemyUnitsMovedThrough(
            IReadOnlyList<ModelMoveEntry> moves, DataBinding<UnitData> movingUnit, IGameContext gameContext)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<DataBinding<UnitData>> crossed = new List<DataBinding<UnitData>>();
            foreach (ArmyData enemyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyUnit in enemyArmy.UnitBindings)
                {
                    if (enemyUnit.GetValue().GetIsDead()) continue;
                    // #207: same origin-parking gap as GetEnemyModelFootprints - an embarked unit's
                    // models sit at (0,0) and must not register as moved-through.
                    if (!enemyUnit.GetValue().GetIsOnBattlefield()) continue;
                    if (PathPassesThroughUnit(moves, enemyUnit))
                    {
                        crossed.Add(enemyUnit);
                    }
                }
            }

            return crossed;
        }

        private static bool PathPassesThroughUnit(IReadOnlyList<ModelMoveEntry> moves, DataBinding<UnitData> enemyUnit)
        {
            foreach (DataBinding<ModelData> enemyModel in enemyUnit.GetValue().ModelBindings)
            {
                if (!enemyModel.GetIsAlive()) continue;

                ModelData enemy = enemyModel.GetValue();
                // The enemy's true footprint as a zone; the moving base is swept along each path segment against
                // it (#150). For circular bases this reduces to the old combined-radius swept-disc, unchanged.
                IZone enemyZone = enemy.BaseShape.ToZone(enemy.PositionBinding.GetValue(), enemy.Facing);

                foreach (ModelMoveEntry move in moves)
                {
                    if (move.Positions.Count == 0) continue;

                    ModelData movingModel = move.Model.GetValue();
                    IBaseShape movingShape = movingModel.BaseShape;
                    Float2 movingFacing = movingModel.Facing;
                    Position segStartPos = movingModel.PositionBinding.GetValue();
                    Float2 segmentStart = new Float2(segStartPos.x, segStartPos.z);

                    foreach (Position step in move.Positions)
                    {
                        Float2 segmentEnd = new Float2(step.x, step.z);
                        if (SweptBaseGeometry.DoesSweptBaseIntersectZone(enemyZone, segmentStart, segmentEnd, movingShape, movingFacing))
                        {
                            return true;
                        }
                        segmentStart = segmentEnd;
                    }
                }
            }

            return false;
        }

        public static void ValidateChargeReach(List<ModelMoveEntry> moves, float maxRushDistance,
            IEnumerable<EnemyModelFootprint> enemyFootprints, ref List<ReasonForInvalidMove> errors)
            => ValidateChargeReach(moves, _ => maxRushDistance, enemyFootprints, ref errors);

        private static void ValidateChargeReach(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, float> maxRushDistanceFor,
            IEnumerable<EnemyModelFootprint> enemyFootprints, ref List<ReasonForInvalidMove> errors)
        {
            Dictionary<ModelMoveEntry, float> totalDistances = GetTotalMoveDistances(moves);

            //If nobody exceeds their own Rush cap, the rule doesn't apply.
            bool anyBeyondRush = totalDistances.Any(kvp => kvp.Value > maxRushDistanceFor(kvp.Key) + 0.0001f);
            if (!anyBeyondRush) return;

            //At least one model in the unit must end within melee range of an enemy model (horizontal).
            //#029: an Aircraft can't be charged, so reaching it doesn't justify a beyond-Rush charge move.
            List<Position> enemies = enemyFootprints?.Where(f => !f.Uncontactable).Select(f => f.Center).ToList()
                ?? new List<Position>();
            float meleeRange = GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL;

            bool anyInMelee = moves.Any(move =>
            {
                Position end = move.Positions.Count == 0
                    ? move.Model.GetValue().PositionBinding.GetValue()
                    : move.Positions[move.Positions.Count - 1];

                return enemies.Any(e => Position.GetDistance2D(end, e) <= meleeRange + 0.0001f);
            });

            if (!anyInMelee)
            {
                //Attach the violation to the first model that went beyond its own Rush.
                ModelMoveEntry culprit = totalDistances.First(kvp => kvp.Value > maxRushDistanceFor(kvp.Key) + 0.0001f).Key;
                errors.Add(new ReasonForInvalidMove(EErrorReasonType.ChargeRangeRequiresMeleeReach, culprit.Model));
            }
        }

        private static Dictionary<ModelMoveEntry, float> GetTotalMoveDistances(List<ModelMoveEntry> moves)
        {
            Dictionary<ModelMoveEntry, float> distances = new Dictionary<ModelMoveEntry, float>();

            foreach (ModelMoveEntry modelEntry in moves)
            {
                List<Position> path = modelEntry.Positions; //Shorthand.

                if (path.Count == 0)
                {
                    distances.Add(modelEntry, 0.0f);
                    continue;
                }

                //Get the distance from the start to the first step.
                float distanceMoved = Position.GetDistance3D(modelEntry.Model.GetValue().PositionBinding, path[0]);

                for (int i = 0; i < path.Count - 1; i++) //Move along the rest of the steps.
                {
                    distanceMoved += Position.GetDistance3D(path[i], path[i + 1]);
                }

                distances.Add(modelEntry, distanceMoved);
            }

            return distances;
        }

        private static void ValidateOutOfMoveRange(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, float> maxDistanceFor, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            Dictionary<ModelMoveEntry, float> totalMoveDistances = GetTotalMoveDistances(moves);

            foreach (KeyValuePair<ModelMoveEntry, float> kvp in totalMoveDistances)
            {
                if (kvp.Value > maxDistanceFor(kvp.Key))
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.OutOfMoveRange, kvp.Key.Model));
                }
            }
        }

        private static void ValidateMovingThroughImpassibleTerrain(List<ModelMoveEntry> moves,
            IEnumerable<ITerrain>? terrain, bool ignoresImpassibleTerrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;
            // Flying (AllTerrain scope) flies over impassible terrain — its path may cross an impassible piece
            // (it still can't end stacked on enemies; coherency + the standoff checks remain in force).
            if (ignoresImpassibleTerrain) return;

            //Snapshot impassable pieces so each model walk doesn't re-enumerate.
            List<ITerrain> impassable = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            if (impassable.Count == 0) return;

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                var model = move.Model.GetValue();
                IBaseShape baseShape = model.BaseShape;
                Float2 facing = model.Facing;
                Position startPos = model.PositionBinding.GetValue();
                Float2 segmentStart = new Float2(startPos.x, startPos.z);

                bool blocked = false;
                for (int i = 0; i < move.Positions.Count && !blocked; i++)
                {
                    Position stepPos = move.Positions[i];
                    Float2 segmentEnd = new Float2(stepPos.x, stepPos.z);

                    // A stationary (zero-length) step isn't moving THROUGH terrain — skip it. Otherwise a model
                    // already sitting within its base radius of impassable terrain self-flags even on a hold,
                    // which defeats the AI's hold-in-place fallback and crashes DefinePathStage (no valid move).
                    if (IsZeroLengthSegment(segmentStart, segmentEnd)) continue;

                    foreach (ITerrain piece in impassable)
                    {
                        //Sweep the model's true base footprint (shape + facing) along the segment so base
                        //overlap — not just the centre crossing — counts as moving through impassable terrain (#150).
                        if (SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, segmentStart, segmentEnd, baseShape, facing))
                        {
                            reasonsForInvalidMove.Add(
                                new ReasonForInvalidMove(EErrorReasonType.MovingThroughImpassibleTerrain, move.Model));
                            blocked = true;
                            break;
                        }
                    }

                    segmentStart = segmentEnd;
                }
            }
        }

        // True when a path segment has effectively no length (a held / stationary step). Such a step can't
        // move a model "through" terrain, so the terrain validators skip it (see the impassible-terrain note).
        private const float ZERO_MOVE_EPSILON_SQ = 1e-8f;
        private static bool IsZeroLengthSegment(Float2 a, Float2 b)
        {
            float dx = b.X - a.X, dz = b.Y - a.Y;
            return dx * dx + dz * dz < ZERO_MOVE_EPSILON_SQ;
        }

        public static bool DoesPathCrossDangerousTerrain(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => DoesPathCrossTerrainPieces(move, terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList());

        /// <summary>Public sibling of the Dangerous check (#155): whether the move's swept path touches any
        /// Difficult-flagged piece. Lets a move-preview resolver know a model's committed waypoints have
        /// already crossed Difficult terrain, so the rest of its move is subject to
        /// <see cref="GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/> (see
        /// <see cref="ClampTravelForDifficultTerrain"/>).</summary>
        public static bool DoesPathCrossDifficultTerrain(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => DoesPathCrossTerrainPieces(move, terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList());

        private static bool DoesPathCrossTerrainPieces(ModelMoveEntry move, List<ITerrain> pieces)
        {
            if (pieces.Count == 0 || move.Positions.Count == 0) return false;

            var model = move.Model.GetValue();
            IBaseShape baseShape = model.BaseShape;
            Float2 facing = model.Facing;
            Position startPos = model.PositionBinding.GetValue();
            Float2 segmentStart = new Float2(startPos.x, startPos.z);

            for (int i = 0; i < move.Positions.Count; i++)
            {
                Float2 segmentEnd = new Float2(move.Positions[i].x, move.Positions[i].z);
                if (IsZeroLengthSegment(segmentStart, segmentEnd)) continue; // a hold doesn't cross terrain
                foreach (ITerrain piece in pieces)
                {
                    if (SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, segmentStart, segmentEnd, baseShape, facing))
                        return true;
                }
                segmentStart = segmentEnd;
            }

            return false;
        }

        /// <summary>Safety margin the difficult-terrain preview clamp keeps below the exact limits — both the
        /// 6" cap and the stop-short-of-the-edge distance — so a clamped endpoint re-validated with exact
        /// constants can't fail on float drift (#155).</summary>
        public const float DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES = 0.01f;

        /// <summary>Why (and whether) <see cref="ClampTravelForDifficultTerrainDetailed"/> shortened a segment
        /// (#155) — lets a resolver show the right on-screen warning.</summary>
        public enum EDifficultClampKind
        {
            /// <summary>Difficult terrain didn't constrain this segment.</summary>
            NotLimited,
            /// <summary>The model moves through Difficult terrain, so its total move is held to the 6" cap.</summary>
            CappedCrossing,
            /// <summary>The model had already moved too far to afford entering, so it stops short of the edge.</summary>
            StoppedShortOfEdge,
        }

        /// <summary>Result of the difficult-terrain preview clamp: how far the segment may travel, and why it
        /// was (or wasn't) shortened (#155).</summary>
        public readonly struct DifficultClampResult
        {
            public readonly float AllowedInches;
            public readonly EDifficultClampKind Kind;
            public DifficultClampResult(float allowedInches, EDifficultClampKind kind)
            { AllowedInches = allowedInches; Kind = kind; }
        }

        /// <summary>
        /// How far a model may actually travel along one straight preview segment, honouring the
        /// difficult-terrain move cap (#155) — the enforcement mirror of
        /// <see cref="ValidateMovingThroughDifficultTerrain"/>, for resolvers that clamp a live ghost instead
        /// of reporting an error after the fact. Band/charge caps are the caller's job — the returned distance
        /// only ever shrinks the segment. See <see cref="ClampTravelForDifficultTerrainDetailed"/> for the
        /// per-case reason.
        /// </summary>
        public static float ClampTravelForDifficultTerrain(Float2 segmentStart, Float2 segmentEnd,
            float traveledBeforeSegmentInches, bool pathAlreadyCrossedDifficultTerrain,
            IBaseShape baseShape, Float2 facing,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain)
            => ClampTravelForDifficultTerrainDetailed(segmentStart, segmentEnd, traveledBeforeSegmentInches,
                pathAlreadyCrossedDifficultTerrain, baseShape, facing, terrain, ignoresDifficultTerrain).AllowedInches;

        /// <summary>
        /// As <see cref="ClampTravelForDifficultTerrain"/>, but also reports WHY the segment was shortened so a
        /// resolver can warn accordingly (#155). Cases:
        /// <list type="bullet">
        /// <item>No Difficult terrain in play, a zero-length segment, or
        /// <paramref name="ignoresDifficultTerrain"/> (Strider/Flying): full segment,
        /// <see cref="EDifficultClampKind.NotLimited"/>.</item>
        /// <item>The path so far has already crossed Difficult terrain
        /// (<paramref name="pathAlreadyCrossedDifficultTerrain"/>) OR this segment enters it with cap room to
        /// spare: the whole move is capped at
        /// <see cref="GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/> total —
        /// <see cref="EDifficultClampKind.CappedCrossing"/>.</item>
        /// <item>The segment would enter Difficult terrain but the model has already moved too far to afford
        /// entering: it stops just short of the edge — <see cref="EDifficultClampKind.StoppedShortOfEdge"/>.</item>
        /// </list>
        /// </summary>
        public static DifficultClampResult ClampTravelForDifficultTerrainDetailed(Float2 segmentStart, Float2 segmentEnd,
            float traveledBeforeSegmentInches, bool pathAlreadyCrossedDifficultTerrain,
            IBaseShape baseShape, Float2 facing,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain)
        {
            float dx = segmentEnd.X - segmentStart.X, dz = segmentEnd.Y - segmentStart.Y;
            float desired = MathF.Sqrt(dx * dx + dz * dz);
            if (ignoresDifficultTerrain || desired <= 0f || terrain == null)
                return new DifficultClampResult(desired, EDifficultClampKind.NotLimited);

            List<ITerrain> difficult = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList();
            if (difficult.Count == 0) return new DifficultClampResult(desired, EDifficultClampKind.NotLimited);

            float capRemaining = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES
                - traveledBeforeSegmentInches - DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES;

            if (pathAlreadyCrossedDifficultTerrain)
                return new DifficultClampResult(Math.Clamp(capRemaining, 0f, desired), EDifficultClampKind.CappedCrossing);

            // Farthest travel along the segment before the swept base first touches any difficult piece.
            float entry = desired;
            foreach (ITerrain piece in difficult)
            {
                entry = MathF.Min(entry, SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                    piece.Shape, segmentStart, segmentEnd, baseShape, facing));
                if (entry <= 0f) break;
            }
            if (entry >= desired) return new DifficultClampResult(desired, EDifficultClampKind.NotLimited); // never enters

            if (capRemaining > entry)
                return new DifficultClampResult(MathF.Min(desired, capRemaining), EDifficultClampKind.CappedCrossing);
            return new DifficultClampResult(
                Math.Clamp(entry - DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES, 0f, desired),
                EDifficultClampKind.StoppedShortOfEdge);
        }

        private static void ValidateMovingThroughDifficultTerrain(List<ModelMoveEntry> moves,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;
            // Strider (and a future Flying rule) waive the difficult-terrain move cap entirely — the unit
            // may cross Difficult terrain without its move being limited to DIFFICULT_TERRAIN_MOVE_CAP_INCHES.
            if (ignoresDifficultTerrain) return;

            List<ITerrain> difficult = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList();
            if (difficult.Count == 0) return;

            Dictionary<ModelMoveEntry, float> distances = GetTotalMoveDistances(moves);

            foreach (ModelMoveEntry move in moves)
            {
                if (DoesPathCrossTerrainPieces(move, difficult)
                    && distances[move] > GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)
                {
                    reasonsForInvalidMove.Add(
                        new ReasonForInvalidMove(EErrorReasonType.ExceededDifficultTerrainMoveLimit, move.Model));
                }
            }
        }

        // Float slack so a charge that lands exactly base-to-base isn't rejected by sub-thousandth rounding.
        private const float ENEMY_PROXIMITY_EPSILON_INCHES = 0.001f;

        // A move ending within this base-to-base gap of an enemy counts as "in base contact" (a charge),
        // which is legal; the forbidden standoff band is (this, ENEMY_STANDOFF_DISTANCE_INCHES). Generous
        // enough to absorb click/float imprecision when a player drags a charger up to touch an enemy.
        private const float ENEMY_CONTACT_TOLERANCE_INCHES = 0.1f;

        /// <summary>
        /// Two related rules (GF movement):
        /// <list type="bullet">
        /// <item>A model may not move <i>through</i> an enemy base — its swept base may not overlap an enemy
        /// base mid-path, nor end stacked on one.</item>
        /// <item>A model that isn't charging must end at least <see cref="GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES"/>
        /// (base-to-base) from every enemy. "Charging" is detected geometrically: a move that ends in base
        /// contact with an enemy unit waives the standoff for that whole unit (so reaching one model of a
        /// multi-model unit doesn't fail you for being within 1" of its other models).</item>
        /// </list>
        /// Only moves that actually close the distance are penalised, so a model already inside an enemy's
        /// reach (e.g. left there by a pile-in/consolidation move) can still move away or hold without
        /// being trapped into an impossible-to-satisfy state.
        /// </summary>
        private static void ValidateMovingThroughEnemyUnits(List<ModelMoveEntry> moves,
            IReadOnlyList<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (enemyFootprints == null || enemyFootprints.Count == 0) return;

            //First pass: which enemy units does this move charge into? A unit is engaged if any moving model
            //ends within melee range of one of its models — the same completed-charge test ValidateChargeReach
            //uses. Charging a unit waives the standoff for that whole unit (you legitimately end within 1" of
            //all its models, not just the one you reached).
            HashSet<int> engagedUnitKeys = new HashSet<int>();
            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;
                Position end = move.Positions[move.Positions.Count - 1];

                foreach (EnemyModelFootprint enemy in enemyFootprints)
                {
                    // #029: an Aircraft (uncontactable) is never "engaged" — a charger can't end in contact with
                    // it, so it stays subject to the standoff + ending-stacked rejections below (can't be charged).
                    if (enemy.Uncontactable) continue;
                    if (Position.GetDistance2D(end, enemy.Center)
                        <= GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL + ENEMY_PROXIMITY_EPSILON_INCHES)
                        engagedUnitKeys.Add(enemy.UnitKey);
                }
            }

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                var movingModel = move.Model.GetValue();
                IBaseShape movingShape = movingModel.BaseShape;
                Float2 movingFacing = movingModel.Facing;
                Position start = movingModel.PositionBinding.GetValue();
                Position end = move.Positions[move.Positions.Count - 1];

                bool flaggedThrough = false;
                bool flaggedStandoff = false;

                foreach (EnemyModelFootprint enemy in enemyFootprints)
                {
                    // Start/end base-to-base gaps use the true, facing-oriented footprints (#150); for circular
                    // bases this is exactly the old `distance − (rMoving + rEnemy)`, so circle behaviour is
                    // unchanged, and a rotated rectangular base measures by its real outline.
                    float startGap = BaseShapeGeometry.SurfaceGap2D(movingShape, start, movingFacing, enemy.BaseShape, enemy.Center, enemy.Facing);
                    float endGap = BaseShapeGeometry.SurfaceGap2D(movingShape, end, movingFacing, enemy.BaseShape, enemy.Center, enemy.Facing);
                    bool movedCloser = endGap < startGap - ENEMY_PROXIMITY_EPSILON_INCHES;

                    // #029: an Aircraft can't be moved into base contact with — a move that closes to within the
                    // standoff distance of it (the base-contact zone OR the standoff band) is rejected, so it can
                    // never be charged or stacked on. Units may still pass UNDER it (the through-check is skipped),
                    // and a unit it flew adjacent to isn't trapped (only moves that close the gap are penalised).
                    if (enemy.Uncontactable)
                    {
                        if (!flaggedStandoff && movedCloser
                            && endGap < GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES - ENEMY_PROXIMITY_EPSILON_INCHES)
                        {
                            reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.EndedTooCloseToEnemy, move.Model));
                            flaggedStandoff = true;
                        }
                        continue;
                    }

                    //Pass-through (#150, shape- and facing-aware): the base starts CLEAR of this enemy and ends
                    //CLEAR of it, yet its swept footprint crosses the enemy's base somewhere along the path — so
                    //it must have gone in one side and out the other. The same swept-zone test the Strafing
                    //through-check uses (exact for rectangles at any facing; a circle reduces to the old swept
                    //disc). A clean charge ends in CONTACT (endGap ≈ 0), so it's not "ends clear" and is handled
                    //by the ending-stacked / standoff rules below, not here. A model that begins in contact
                    //(startGap ≤ tol) isn't newly penalised for its pre-existing position. canMoveThroughEnemies
                    //(Strafing fly-over) is exempt — it may path through an enemy base.
                    if (!canMoveThroughEnemies && !flaggedThrough
                        && startGap > ENEMY_CONTACT_TOLERANCE_INCHES && endGap > ENEMY_CONTACT_TOLERANCE_INCHES)
                    {
                        IZone enemyZone = enemy.BaseShape.ToZone(enemy.Center, enemy.Facing);
                        Position segStart = start;
                        foreach (Position step in move.Positions)
                        {
                            Float2 a = new Float2(segStart.x, segStart.z);
                            Float2 b = new Float2(step.x, step.z);
                            if (SweptBaseGeometry.DoesSweptBaseIntersectZone(enemyZone, a, b, movingShape, movingFacing))
                            {
                                reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.MovingThroughEnemyUnit, move.Model));
                                flaggedThrough = true;
                                break;
                            }
                            segStart = step;
                        }
                    }

                    //Ending stacked on an enemy is never allowed, even charging.
                    if (!flaggedThrough && endGap < -ENEMY_CONTACT_TOLERANCE_INCHES && movedCloser)
                    {
                        reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.MovingThroughEnemyUnit, move.Model));
                        flaggedThrough = true;
                    }

                    //Standoff: ending in the (contact, standoff) band is illegal unless this unit is being charged.
                    if (!flaggedStandoff && movedCloser && !engagedUnitKeys.Contains(enemy.UnitKey)
                        && endGap > ENEMY_CONTACT_TOLERANCE_INCHES
                        && endGap < GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES - ENEMY_PROXIMITY_EPSILON_INCHES)
                    {
                        reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.EndedTooCloseToEnemy, move.Model));
                        flaggedStandoff = true;
                    }

                    if (flaggedThrough && flaggedStandoff) break;
                }
            }
        }

        // Float slack on the cohesion limits so a move that lands a model exactly on the 1"/9" boundary
        // isn't rejected by sub-thousandth rounding in the 3D base-to-base distance.
        private const float COHESION_EPSILON_INCHES = 0.001f;

        private static void ValidateCoherency(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            List<DataBinding<ModelData>> models = new List<DataBinding<ModelData>>();
            List<Position> positions = new List<Position>();

            //Figure out where all the models will be after moving. Dead models are out of play — they
            //leave a hole in the formation rather than anchoring it, so a casualty's last position must
            //not count toward cohesion (it would wrongly fail the survivors for being "too far" from a corpse).
            foreach (ModelMoveEntry moveEntry in moves)
            {
                if (moveEntry.Model.GetValue().GetIsAlive() == false)
                {
                    continue;
                }

                models.Add(moveEntry.Model);
                positions.Add(moveEntry.Positions.Count > 0
                    ? moveEntry.Positions.Last()
                    : moveEntry.Model.GetValue().PositionBinding.GetValue());
            }

            //If there's just one living model, there's nothing to compare.
            if (models.Count <= 1)
            {
                return;
            }

            //Check each model's distance against all the others, for both kinds of coherency.
            //As of 3.4.0, each model must be within 1" of another model and within 9" of every other model.

            float[] nearestDistances = new float[models.Count];
            float[] farthestDistances = new float[models.Count];

            for (int i = 0; i < models.Count; i++)
            {
                nearestDistances[i] = float.PositiveInfinity;
                farthestDistances[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < models.Count; i++)
            {
                for (int j = i + 1; j < models.Count; j++)
                {
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(positions[i], positions[j],
                        models[i].GetValue().BaseShape, models[i].GetValue().Facing,
                        models[j].GetValue().BaseShape, models[j].GetValue().Facing);

                    nearestDistances[i] = Math.Min(distance, nearestDistances[i]);
                    farthestDistances[i] = Math.Max(distance, farthestDistances[i]);

                    nearestDistances[j] = Math.Min(distance, nearestDistances[j]);
                    farthestDistances[j] = Math.Max(distance, farthestDistances[j]);
                }
            }

            for (int i = 0; i < models.Count; i++)
            {
                if (nearestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + COHESION_EPSILON_INCHES)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAnyUnitModel, models[i]));
                }

                if (farthestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES + COHESION_EPSILON_INCHES)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAllUnitModels, models[i]));
                }
            }
        }


        /// <summary>
        /// Lenient coherency check for consolidation (#159): tolerates a break that already existed at the
        /// models' CURRENT (pre-move) positions. A model is flagged only when the move makes its coherency
        /// worse — its post-move nearest-neighbour gap exceeds max(1", its pre-move nearest gap), or its
        /// post-move farthest gap exceeds max(9", its pre-move farthest gap). So a mid-unit casualty that
        /// leaves survivors &gt;1" apart doesn't trap the unit: a hold (positions unchanged) is always valid,
        /// and any re-forming move that shrinks the gaps is valid, but a move that scatters the unit further
        /// is still rejected. Mirrors the enemy-standoff rule's "only penalise moves that close the distance".
        /// </summary>
        private static void ValidateCoherencyNotWorsened(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            List<DataBinding<ModelData>> models = new List<DataBinding<ModelData>>();
            List<Position> before = new List<Position>();
            List<Position> after = new List<Position>();

            foreach (ModelMoveEntry moveEntry in moves)
            {
                if (moveEntry.Model.GetValue().GetIsAlive() == false) continue;

                models.Add(moveEntry.Model);
                before.Add(moveEntry.Model.GetValue().PositionBinding.GetValue());
                after.Add(moveEntry.Positions.Count > 0
                    ? moveEntry.Positions.Last()
                    : moveEntry.Model.GetValue().PositionBinding.GetValue());
            }

            if (models.Count <= 1) return;

            ComputeCohesionExtents(models, before, out float[] nearestBefore, out float[] farthestBefore);
            ComputeCohesionExtents(models, after, out float[] nearestAfter, out float[] farthestAfter);

            for (int i = 0; i < models.Count; i++)
            {
                float nearestLimit = Math.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES,
                    nearestBefore[i]) + COHESION_EPSILON_INCHES;
                if (nearestAfter[i] > nearestLimit)
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAnyUnitModel, models[i]));

                float farthestLimit = Math.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES,
                    farthestBefore[i]) + COHESION_EPSILON_INCHES;
                if (farthestAfter[i] > farthestLimit)
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAllUnitModels, models[i]));
            }
        }

        // Per-model nearest- and farthest-neighbour base-to-base 3D distances at the given positions
        // (index-aligned with <paramref name="models"/>). Shared by the not-worsened coherency check.
        private static void ComputeCohesionExtents(List<DataBinding<ModelData>> models, List<Position> positions,
            out float[] nearest, out float[] farthest)
        {
            int count = models.Count;
            nearest = new float[count];
            farthest = new float[count];
            for (int i = 0; i < count; i++) { nearest[i] = float.PositiveInfinity; farthest[i] = float.NegativeInfinity; }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(positions[i], positions[j],
                        models[i].GetValue().BaseShape, models[i].GetValue().Facing,
                        models[j].GetValue().BaseShape, models[j].GetValue().Facing);

                    nearest[i] = Math.Min(distance, nearest[i]);
                    farthest[i] = Math.Max(distance, farthest[i]);
                    nearest[j] = Math.Min(distance, nearest[j]);
                    farthest[j] = Math.Max(distance, farthest[j]);
                }
            }
        }

        public static void AssertModelInUnit(DataBinding<UnitData> unit, DataBinding<ModelData> model,
            [CallerMemberName] string methodName = null)
        {
            if (model == default)
            {
                throw new ArgumentException($"{nameof(PathTemplate)}.{methodName} called with null model.");
            }

            if (unit.GetValue().ModelBindings.Contains(model) == false)
            {
                throw new ArgumentException($"{nameof(PathTemplate)}.{methodName} called with model not in the unit.");
            }
        }

        public static string ErrorReasonToString(EErrorReasonType reason)
        {
            switch(reason)
            {
                case EErrorReasonType.OutOfMoveRange:
                    return "Unit moving too far";
                case EErrorReasonType.MovingThroughEnemyUnit:
                    return "Moves through an enemy unit";
                case EErrorReasonType.EndedTooCloseToEnemy:
                    return $"Ends within {GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES}\" of an enemy without charging it";
                case EErrorReasonType.MovingThroughImpassibleTerrain:
                    return "Moves through impassible terrain";
                case EErrorReasonType.ExceededDifficultTerrainMoveLimit:
                    return $"Moved more than {GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES}\" through difficult terrain";
                case EErrorReasonType.TooFarFromAnyUnitModel:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES} " + 
                        "inches from the closest model";
                case EErrorReasonType.TooFarFromAllUnitModels:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES} " +
                        "inches from another model in the unit";
                case EErrorReasonType.ChargeRangeRequiresMeleeReach:
                    return $"A model moved beyond Rush range, but no model ends within {GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL}\" of an enemy";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }


    public readonly struct ReasonForInvalidMove
    {
        public readonly EErrorReasonType ErrorReasonType;
        public readonly DataBinding<ModelData> RelevantModel;


        public ReasonForInvalidMove(EErrorReasonType errorReasonType, DataBinding<ModelData> relevantModel)
        {
            ErrorReasonType = errorReasonType;
            RelevantModel = relevantModel;
        }

        public override string ToString() => MovementUtilities.ErrorReasonToString(ErrorReasonType);
    }

    public enum EErrorReasonType
    {
        OutOfMoveRange,
        MovingThroughImpassibleTerrain,
        ExceededDifficultTerrainMoveLimit,
        MovingThroughEnemyUnit,
        TooFarFromAnyUnitModel,
        TooFarFromAllUnitModels,
        ChargeRangeRequiresMeleeReach,
        EndedTooCloseToEnemy
    }

    /// <summary>
    /// A living enemy model's base footprint for movement validation: where it is, how big its base is, and
    /// a key identifying which enemy unit it belongs to (shared by all models of that unit within one
    /// footprint list). See <see cref="MovementUtilities.GetEnemyModelFootprints"/>.
    /// </summary>
    public readonly struct EnemyModelFootprint
    {
        public readonly Position Center;
        public readonly float BaseRadiusInches;
        // The enemy's true base footprint and facing (#150), used shape-aware by both the end-state gap checks
        // and the mid-path swept pass-through — so a rectangular enemy is measured by its real oriented outline.
        public readonly IBaseShape BaseShape;
        public readonly Float2 Facing;
        public readonly int UnitKey;

        /// <summary>
        /// #029: this footprint belongs to a unit that can't be moved into base contact with (Aircraft). The
        /// move validator never treats such a unit as "charged" (engaged), so a move may not end stacked on it
        /// nor within the standoff band — i.e. it can't be charged or contacted. Default false.
        /// </summary>
        public readonly bool Uncontactable;

        public EnemyModelFootprint(Position center, float baseRadiusInches, int unitKey, bool uncontactable = false,
            IBaseShape? baseShape = null, Float2? facing = null)
        {
            Center = center;
            BaseRadiusInches = baseRadiusInches;
            // No explicit shape (radius-only callers / tests) → a circle of that radius, i.e. the prior behaviour.
            BaseShape = baseShape ?? new CircleBase(baseRadiusInches);
            Facing = facing ?? new Float2(0f, 1f); // forward (+Z) — the axis-aligned default for radius-only callers
            UnitKey = unitKey;
            Uncontactable = uncontactable;
        }
    }
}
