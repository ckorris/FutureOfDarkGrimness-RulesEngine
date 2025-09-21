
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using System.IO;

namespace FDG
{
    /// <summary>
    /// Lets you define a path for a bunch of models in order to submit for a move, with built-in validation
    /// for whether or not you can move that way.
    /// </summary>
    public class PathTemplate
    {
        public IReadOnlyDictionary<IModel, IReadOnlyList<Position>> CurrentPaths => _paths.ToDictionary(
                    kvp => kvp.Key.GetValue() as IModel,
                    kvp => (IReadOnlyList<Position>)kvp.Value);

        private Dictionary<DataBinding<ModelData>, List<Position>> _paths = new Dictionary<DataBinding<ModelData>, List<Position>>();

        public IUnit Unit => _unit.GetValue();

        public float MaxAdvanceDistance => _maxDistanceInches / 2f; //TODO: Things can buff one distance but not the other.

        public float MaxChargeDistance => _maxDistanceInches; 

        private DataBinding<UnitData> _unit;
        private float _maxDistanceInches;

        public PathTemplate(DataBinding<UnitData> unit, float maxDistanceInches)
        {
            _unit = unit;
            _maxDistanceInches = maxDistanceInches;

            foreach (DataBinding<ModelData> model in unit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                _paths.Add(model, new List<Position>());
            }
        }

        public bool ValidateAll(out List<ReasonForInvalidMove> invalidReasons)
        {
            List<ModelMoveEntry> resultsList = GetResultsAsList();
            return MovementUtilities.ValidatePaths(resultsList, _maxDistanceInches, out invalidReasons);
        }

        public void AddStep(IModel model, Position nextStep)
        {
            DataBinding<ModelData> modelData = _paths.Keys.First(m => m.GetValue() == model);

            MovementUtilities.AssertModelInUnit(_unit, modelData);

            _paths[modelData].Add(nextStep);
        }

        public void RemoveLastStep(IModel model)
        {
            DataBinding<ModelData> modelData = _paths.Keys.First(m => m.GetValue() == model);

            MovementUtilities.AssertModelInUnit(_unit, modelData);

            List<Position> modelSteps = _paths[modelData];

            if (modelSteps.Count == 0)
            {
                throw new System.InvalidOperationException($"Tried to remove the last step for a model that had no steps listed.");
            }

            modelSteps.RemoveAt(modelSteps.Count - 1);
        }

        public void ClearModelSteps(IModel model)
        {
            DataBinding<ModelData> modelData = _paths.Keys.First(m => m.GetValue() == model);


            MovementUtilities.AssertModelInUnit(_unit, modelData);

            _paths[modelData].Clear();
        }

        public void ClearAllSteps()
        {
            foreach (List<Position> path in _paths.Values)
            {
                path.Clear();
            }
        }

        public float GetTotalDistanceMoved(IModel model)
        {
            IReadOnlyList<Position> path = _paths.First(kvp => kvp.Key.GetValue() == model).Value;

            if (path.Count == 0) return 0;

            float totalDistance = 0;
            Position currentPos = model.Position;

            //Distance from model to first waypoint.
            totalDistance += Position.GetDistance3D(currentPos, path[0]);

            //Distance between waypoints.
            for (int i = 1; i < path.Count; i++)
            {
                totalDistance += Position.GetDistance3D(path[i - 1], path[i]);
            }

            return totalDistance;
        }

        public Position GetModelLastPathPosition(IModel model)
        {
            IReadOnlyList<Position> path = _paths.First(kvp => kvp.Key.GetValue() == model).Value;

            if (path.Count == 0)
            {
                return model.Position;
            }

            return path.Last();
        }

        public List<ModelMoveEntry> GetResultsAsList()
        {
            List<ModelMoveEntry> results = new List<ModelMoveEntry>(_paths.Count);
            foreach(KeyValuePair< DataBinding<ModelData>, List<Position>> kvp in _paths)
            {
                results.Add(new ModelMoveEntry(kvp.Key, kvp.Value));
            }

            return results;
        }
    }

}
