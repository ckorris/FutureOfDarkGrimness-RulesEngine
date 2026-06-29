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

            ValidateOutOfMoveRange(moves, maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ref errors);
            //This overload is only reached with terrain: null (the no-terrain convenience form), so the
            //difficult-terrain check is a no-op regardless — no Strider flag to thread here.
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
        /// <paramref name="ignoresDifficultTerrain"/> waives the difficult-terrain move cap (Strider).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherency(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Back-compat charge overload that assumes the mover may not move through enemies
        /// (<c>canMoveThroughEnemies: false</c>) nor ignore difficult terrain
        /// (<c>ignoresDifficultTerrain: false</c>). Kept so callers/tests that never need either rule-derived
        /// flag don't have to thread them (mirrors the no-enemy convenience overloads above).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, maxRushDistance, maxDistanceInches, enemyFootprints,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, terrain, out errors);

        /// <summary>
        /// Full Move-action validation: paths must stay within the hard cap (Charge distance),
        /// and any path that exceeds the Rush distance requires at least one model to end within
        /// melee range of an enemy model. <paramref name="canMoveThroughEnemies"/> waives the
        /// pass-through block for fly-over units (Strafing); <paramref name="ignoresDifficultTerrain"/>
        /// waives the difficult-terrain move cap (Strider).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherency(moves, ref errors);
            ValidateChargeReach(moves, maxRushDistance, enemies, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Every living enemy model's base footprint (centre + radius), tagged with a per-unit key so the
        /// move-through / standoff check can tell which models belong to the same enemy unit (a charge into
        /// one model of a unit legitimately ends within the 1" standoff of that whole unit). The key is only
        /// stable within the returned list. Enemies are everyone not on the moving unit's team.
        /// </summary>
        public static List<EnemyModelFootprint> GetEnemyModelFootprints(DataBinding<UnitData> movingUnit, IGameContext gameContext)
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
                    bool anyLiving = false;
                    foreach (DataBinding<ModelData> enemyModel in enemyUnit.ModelBindings()
                        .Where(m => m.GetIsAlive()))
                    {
                        ModelData md = enemyModel.GetValue();
                        footprints.Add(new EnemyModelFootprint(md.PositionBinding.GetValue(), md.BaseRadiusInches, unitKey));
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

                Position enemyCenter = enemyModel.GetValue().PositionBinding.GetValue();
                float enemyRadius = enemyModel.GetValue().BaseRadiusInches;

                foreach (ModelMoveEntry move in moves)
                {
                    if (move.Positions.Count == 0) continue;

                    float contactDistance = enemyRadius + move.Model.GetValue().BaseRadiusInches;
                    Position segmentStart = move.Model.GetValue().PositionBinding.GetValue();

                    foreach (Position step in move.Positions)
                    {
                        if (DistancePointToSegment2D(enemyCenter, segmentStart, step) <= contactDistance)
                        {
                            return true;
                        }
                        segmentStart = step;
                    }
                }
            }

            return false;
        }

        // Shortest 2D (x,z) distance from point p to the segment a->b.
        private static float DistancePointToSegment2D(Position p, Position a, Position b)
            => DistancePointToSegment2D(p, a, b, out _);

        // As above, also reporting the closest point on the segment (so callers can tell an interior
        // crossing from a touch at an endpoint).
        private static float DistancePointToSegment2D(Position p, Position a, Position b, out Float2 closest)
        {
            float abx = b.x - a.x;
            float abz = b.z - a.z;
            float lengthSq = abx * abx + abz * abz;

            float t = lengthSq <= 1e-6f ? 0f : ((p.x - a.x) * abx + (p.z - a.z) * abz) / lengthSq;
            t = Math.Clamp(t, 0f, 1f);

            float closestX = a.x + t * abx;
            float closestZ = a.z + t * abz;
            closest = new Float2(closestX, closestZ);
            float dx = p.x - closestX;
            float dz = p.z - closestZ;

            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static void ValidateChargeReach(List<ModelMoveEntry> moves, float maxRushDistance,
            IEnumerable<EnemyModelFootprint> enemyFootprints, ref List<ReasonForInvalidMove> errors)
        {
            Dictionary<ModelMoveEntry, float> totalDistances = GetTotalMoveDistances(moves);

            //If nobody exceeds the Rush cap, the rule doesn't apply.
            bool anyBeyondRush = totalDistances.Values.Any(d => d > maxRushDistance + 0.0001f);
            if (!anyBeyondRush) return;

            //At least one model in the unit must end within melee range of an enemy model (horizontal).
            List<Position> enemies = enemyFootprints?.Select(f => f.Center).ToList() ?? new List<Position>();
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
                //Attach the violation to the first model that went beyond Rush.
                ModelMoveEntry culprit = totalDistances.First(kvp => kvp.Value > maxRushDistance + 0.0001f).Key;
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

        private static void ValidateOutOfMoveRange(List<ModelMoveEntry> moves, float maxChargeDistance,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            Dictionary<ModelMoveEntry, float> totalMoveDistances = GetTotalMoveDistances(moves);

            foreach (KeyValuePair<ModelMoveEntry, float> kvp in totalMoveDistances)
            {
                if (kvp.Value > maxChargeDistance)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.OutOfMoveRange, kvp.Key.Model));
                }
            }
        }

        private static void ValidateMovingThroughImpassibleTerrain(List<ModelMoveEntry> moves,
            IEnumerable<ITerrain>? terrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;

            //Snapshot impassable pieces so each model walk doesn't re-enumerate.
            List<ITerrain> impassable = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            if (impassable.Count == 0) return;

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                float baseRadius = move.Model.GetValue().BaseRadiusInches;
                Position startPos = move.Model.GetValue().PositionBinding.GetValue();
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
                        //Inflate the footprint by the model's base radius so base overlap (not just
                        //the center crossing) counts as moving through impassable terrain.
                        if (piece.DoesPathIntersectZone(segmentStart, segmentEnd, baseRadius))
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
        {
            List<ITerrain> dangerous = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList();
            if (dangerous.Count == 0 || move.Positions.Count == 0) return false;

            float baseRadius = move.Model.GetValue().BaseRadiusInches;
            Position startPos = move.Model.GetValue().PositionBinding.GetValue();
            Float2 segmentStart = new Float2(startPos.x, startPos.z);

            for (int i = 0; i < move.Positions.Count; i++)
            {
                Float2 segmentEnd = new Float2(move.Positions[i].x, move.Positions[i].z);
                if (IsZeroLengthSegment(segmentStart, segmentEnd)) continue; // a hold doesn't cross terrain
                foreach (ITerrain piece in dangerous)
                {
                    if (piece.DoesPathIntersectZone(segmentStart, segmentEnd, baseRadius))
                        return true;
                }
                segmentStart = segmentEnd;
            }

            return false;
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
                if (move.Positions.Count == 0) continue;

                float baseRadius = move.Model.GetValue().BaseRadiusInches;
                Position startPos = move.Model.GetValue().PositionBinding.GetValue();
                Float2 segmentStart = new Float2(startPos.x, startPos.z);

                bool crossesDifficult = false;
                for (int i = 0; i < move.Positions.Count && !crossesDifficult; i++)
                {
                    Float2 segmentEnd = new Float2(move.Positions[i].x, move.Positions[i].z);
                    if (IsZeroLengthSegment(segmentStart, segmentEnd)) continue; // a hold doesn't cross terrain
                    foreach (ITerrain piece in difficult)
                    {
                        if (piece.DoesPathIntersectZone(segmentStart, segmentEnd, baseRadius))
                        {
                            crossesDifficult = true;
                            break;
                        }
                    }
                    segmentStart = segmentEnd;
                }

                if (crossesDifficult && distances[move] > GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)
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
                    if (Position.GetDistance2D(end, enemy.Center)
                        <= GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL + ENEMY_PROXIMITY_EPSILON_INCHES)
                        engagedUnitKeys.Add(enemy.UnitKey);
                }
            }

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                float movingRadius = move.Model.GetValue().BaseRadiusInches;
                Position start = move.Model.GetValue().PositionBinding.GetValue();
                Position end = move.Positions[move.Positions.Count - 1];

                bool flaggedThrough = false;
                bool flaggedStandoff = false;

                foreach (EnemyModelFootprint enemy in enemyFootprints)
                {
                    float contactDistance = movingRadius + enemy.BaseRadiusInches;
                    float startGap = Position.GetDistance2D(start, enemy.Center) - contactDistance;
                    float endGap = Position.GetDistance2D(end, enemy.Center) - contactDistance;
                    bool movedCloser = endGap < startGap - ENEMY_PROXIMITY_EPSILON_INCHES;

                    //Pass-through: the swept base crosses this enemy's base at an interior point of the path
                    //(not at the model's own start, and not where it ends in legal contact). A clean charge's
                    //closest approach is its destination, so it isn't caught here. A fly-over unit
                    //(canMoveThroughEnemies, e.g. Strafing) is exempt from this block — it may path through an
                    //enemy base — but it is still caught by the ending-stacked check below.
                    if (!canMoveThroughEnemies && !flaggedThrough)
                    {
                        Position segStart = start;
                        foreach (Position step in move.Positions)
                        {
                            float gap = DistancePointToSegment2D(enemy.Center, segStart, step, out Float2 closest) - contactDistance;
                            bool atPathStart = Distance2D(closest, start) <= ENEMY_CONTACT_TOLERANCE_INCHES;
                            bool atPathEnd = Distance2D(closest, end) <= ENEMY_CONTACT_TOLERANCE_INCHES;
                            if (gap < -ENEMY_CONTACT_TOLERANCE_INCHES && !atPathStart && !atPathEnd)
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

        private static float Distance2D(Float2 a, Position b)
        {
            float dx = a.X - b.x;
            float dz = a.Y - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
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
                        models[i].GetValue().BaseShape, models[j].GetValue().BaseShape);

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
        public readonly int UnitKey;

        public EnemyModelFootprint(Position center, float baseRadiusInches, int unitKey)
        {
            Center = center;
            BaseRadiusInches = baseRadiusInches;
            UnitKey = unitKey;
        }
    }
}
