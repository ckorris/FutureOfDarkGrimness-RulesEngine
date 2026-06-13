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
            ValidateMovingThroughDifficultTerrain(moves, terrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, ref errors);
            ValidateCoherency(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Full Move-action validation: paths must stay within the hard cap (Charge distance),
        /// and any path that exceeds the Rush distance requires at least one model to end within
        /// melee range of an enemy model.
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<Position> enemyModelPositions,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            ValidateOutOfMoveRange(moves, maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, ref errors);
            ValidateCoherency(moves, ref errors);
            ValidateChargeReach(moves, maxRushDistance, enemyModelPositions, ref errors);

            return errors.Count == 0;
        }

        public static List<Position> GetEnemyModelPositions(DataBinding<UnitData> movingUnit, IGameContext gameContext)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<Position> positions = new List<Position>();
            foreach (ArmyData enemyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyUnit in enemyArmy.UnitBindings)
                {
                    foreach (DataBinding<ModelData> enemyModel in enemyUnit.ModelBindings()
                        .Where(m => m.GetIsAlive()))
                    {
                        positions.Add(enemyModel.GetValue().PositionBinding.GetValue());
                    }
                }
            }

            return positions;
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
        {
            float abx = b.x - a.x;
            float abz = b.z - a.z;
            float lengthSq = abx * abx + abz * abz;

            float t = lengthSq <= 1e-6f ? 0f : ((p.x - a.x) * abx + (p.z - a.z) * abz) / lengthSq;
            t = Math.Clamp(t, 0f, 1f);

            float closestX = a.x + t * abx;
            float closestZ = a.z + t * abz;
            float dx = p.x - closestX;
            float dz = p.z - closestZ;

            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static void ValidateChargeReach(List<ModelMoveEntry> moves, float maxRushDistance,
            IEnumerable<Position> enemyModelPositions, ref List<ReasonForInvalidMove> errors)
        {
            Dictionary<ModelMoveEntry, float> totalDistances = GetTotalMoveDistances(moves);

            //If nobody exceeds the Rush cap, the rule doesn't apply.
            bool anyBeyondRush = totalDistances.Values.Any(d => d > maxRushDistance + 0.0001f);
            if (!anyBeyondRush) return;

            //At least one model in the unit must end within melee range of an enemy model (horizontal).
            List<Position> enemies = enemyModelPositions?.ToList() ?? new List<Position>();
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
            IEnumerable<ITerrain>? terrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;

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

        private static void ValidateMovingThroughEnemyUnits(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //TODO: Implement.
        }

        private static void ValidateCoherency(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //If there's just one model, there's nothing to compare.
            if (moves.Count <= 1)
            {
                return;
            }

            List<DataBinding<ModelData>> models = new List<DataBinding<ModelData>>();
            List<Position> positions = new List<Position>();

            //Figure out where all the models will be after moving.
            foreach (ModelMoveEntry moveEntry in moves)
            {
                models.Add(moveEntry.Model);
                positions.Add(moveEntry.Positions.Count > 0 
                    ? moveEntry.Positions.Last() 
                    : moveEntry.Model.GetValue().PositionBinding.GetValue());
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
                        models[i].GetValue().BaseRadiusInches, models[j].GetValue().BaseRadiusInches);

                    nearestDistances[i] = Math.Min(distance, nearestDistances[i]);
                    farthestDistances[i] = Math.Max(distance, farthestDistances[i]);

                    nearestDistances[j] = Math.Min(distance, nearestDistances[j]);
                    farthestDistances[j] = Math.Max(distance, farthestDistances[j]);
                }
            }

            for (int i = 0; i < models.Count; i++)
            {
                if (nearestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAnyUnitModel, models[i]));
                }

                if (farthestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES)
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
        ChargeRangeRequiresMeleeReach
    }
}
