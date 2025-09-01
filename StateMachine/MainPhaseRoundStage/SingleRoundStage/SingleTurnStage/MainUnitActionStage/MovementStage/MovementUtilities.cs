using FDG.Data;
using FDG.StageResolution.Requests;
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

        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxChargeDistance, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            ValidateOutOfMoveRange(moves, maxChargeDistance, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, ref errors);
            ValidateMovingThroughEnemyUnits(moves, ref errors);
            ValidateCoherency(moves, ref errors);

            return errors.Count == 0;
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
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //TODO: Implement.
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
                    farthestDistances[i] = Math.Min(distance, nearestDistances[i]);

                    nearestDistances[j] = Math.Min(distance, nearestDistances[j]);
                    farthestDistances[j] = Math.Min(distance, nearestDistances[j]);
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
                case EErrorReasonType.TooFarFromAnyUnitModel:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES} " + 
                        "inches from the closest model";
                case EErrorReasonType.TooFarFromAllUnitModels:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES} " + 
                        "inches from another model in the unit";
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
    }

    public enum EErrorReasonType
    {
        OutOfMoveRange,
        MovingThroughImpassibleTerrain,
        MovingThroughEnemyUnit,
        TooFarFromAnyUnitModel,
        TooFarFromAllUnitModels
    }
}
