
using FDG.Stages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FDG
{
    /// <summary>
    /// Lets you define a path for a bunch of models in order to submit for a move, with built-in validation
    /// for whether or not you can move that way.
    /// </summary>
    public class PathTemplate
    {
        public IReadOnlyDictionary<IModel, IReadOnlyList<Position>> CurrentPaths => _paths.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<Position>)kvp.Value);

        private Dictionary<IModel, List<Position>> _paths = new Dictionary<IModel, List<Position>>();

        private IMovementActionContext _movementContext;

        public PathTemplate(IUnit unit, IMovementActionContext movementContext)
        {
            foreach (IModel model in unit.Models)
            {
                _paths.Add(model, new List<Position>());
            }

            _movementContext = movementContext;
        }

        public void AddStep(IModel model, Position nextStep)
        {
            AssertModelInUnit(model);

            _paths[model].Add(nextStep);
        }

        public void RemoveLastStep(IModel model)
        {
            AssertModelInUnit(model);

            List<Position> modelSteps = _paths[model];

            if (modelSteps.Count == 0)
            {
                throw new System.InvalidOperationException($"Tried to remove the last step for a model that had no steps listed.");
            }

            modelSteps.RemoveAt(modelSteps.Count - 1);
        }

        public void ClearModelSteps(IModel model)
        {
            AssertModelInUnit(model);

            _paths[model].Clear();
        }

        public void ClearAllSteps()
        {
            foreach (List<Position> path in _paths.Values)
            {
                path.Clear();
            }
        }

        public bool ValidatePaths(out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            ValidateOutOfMoveRange(ref errors);
            ValidateMovingThroughImpassibleTerrain(ref errors);
            ValidateMovingThroughEnemyUnits(ref errors);
            ValidateCoherency(ref errors);

            return errors.Count > 0;
        }

        public float GetMaxMoveDistance()
        {
            Dictionary<IModel, float> distances = GetTotalMoveDistances();
            return distances.Values.Max();
        }

        private Dictionary<IModel, float> GetTotalMoveDistances()
        {
            Dictionary<IModel, float> distances = new Dictionary<IModel, float>();

            foreach (IModel model in _paths.Keys)
            {
                List<Position> path = _paths[model];

                if (path.Count == 0)
                {
                    distances.Add(model, 0.0f);
                    continue;
                }

                //Get the distance from the start to the first step.
                float distanceMoved = Position.GetDistance3D(model.Position, path[0]);

                for (int i = 0; i < path.Count - 1; i++) //Move along the rest of the steps.
                {
                    distanceMoved += Position.GetDistance3D(path[i], path[i + 1]);
                }

                distances.Add(model, distanceMoved);
            }

            return distances;
        }

        private void ValidateOutOfMoveRange(ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            Dictionary<IModel, float> totalMoveDistances = GetTotalMoveDistances();

            foreach (KeyValuePair<IModel, float> kvp in totalMoveDistances)
            {
                if(kvp.Value < _movementContext.MaxChargeDistance)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.OutOfMoveRange, kvp.Key));
                }
            }
        }

        private void ValidateMovingThroughImpassibleTerrain(ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //TODO: Implement.
        }

        private void ValidateMovingThroughEnemyUnits(ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //TODO: Implement.
        }

        private void ValidateCoherency(ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            //If there's just one model, there's nothing to compare.
            if(_paths.Count <= 1)
            {
                return;
            }

            List<IModel> models = new List<IModel>();
            List<Position> positions = new List<Position>();

            //Figure out where all the models will be after moving.
            foreach(KeyValuePair<IModel, List<Position>> kvp in _paths)
            {
                models.Add(kvp.Key);
                positions.Add(kvp.Value.Count > 0 ? kvp.Value.Last() : kvp.Key.Position);
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
                        models[i].BaseRadiusInches, models[j].BaseRadiusInches);

                    nearestDistances[i] = Math.Min(distance, nearestDistances[i]);
                    farthestDistances[i] = Math.Min(distance, nearestDistances[i]);

                    nearestDistances[j] = Math.Min(distance, nearestDistances[j]);
                    farthestDistances[j] = Math.Min(distance, nearestDistances[j]);
                }
            }

            for(int i = 0; i < models.Count; i++)
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


        private void AssertModelInUnit(IModel model, [CallerMemberName] string methodName = null)
        {
            if (model == default)
            {
                throw new System.ArgumentException($"{nameof(PathTemplate)}.{methodName} called with null model.");
            }

            if (_paths.Keys.Contains(model) == false)
            {
                throw new System.ArgumentException($"{nameof(PathTemplate)}.{methodName} called with model not in the unit.");
            }
        }
    }

    public readonly struct ReasonForInvalidMove
    {

        public readonly EErrorReasonType ErrorReasonType;
        public readonly IModel RelevantModel;


        public ReasonForInvalidMove(EErrorReasonType errorReasonType, IModel relevantModel)
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
