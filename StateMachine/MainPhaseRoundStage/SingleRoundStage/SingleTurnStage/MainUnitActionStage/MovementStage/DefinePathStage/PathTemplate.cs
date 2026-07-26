
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using System.IO;

namespace FDG
{
    /// <summary>
    /// How <see cref="PathTemplate.GetResultsAsList"/> derives each waypoint's facing from the manual
    /// offset that waypoint was placed with (#282/#283).
    /// </summary>
    public enum EPathFacingDerivation
    {
        /// <summary>Entries carry no facings; the mover keeps its resting facing (validation, skips, AI).</summary>
        None,
        /// <summary>#150: face the direction of travel into each waypoint, rotated by its offset (movement).</summary>
        TravelDirection,
        /// <summary>#215/#283: keep the model's current facing, rotated by each waypoint's offset (consolidation).</summary>
        RotateInPlace,
    }

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

        // #282: the manual facing offset (radians) each waypoint was placed with, 1:1 with _paths. Captured
        // at AddStep so a rotation made later in the plan never re-orients already-committed waypoints.
        private Dictionary<DataBinding<ModelData>, List<float>> _facingOffsets = new Dictionary<DataBinding<ModelData>, List<float>>();

        public IUnit Unit => _unit.GetValue();

        public float MaxAdvanceDistance => _maxAdvanceDistance;

        public float MaxDistanceInches => _maxDistanceInches;

        private DataBinding<UnitData> _unit;
        private float _maxAdvanceDistance;
        private float _maxDistanceInches;

        public PathTemplate(DataBinding<UnitData> unit, float maxAdvanceDistance, float maxDistanceInches)
        {
            _unit = unit;
            _maxAdvanceDistance = maxAdvanceDistance;
            _maxDistanceInches = maxDistanceInches;

            foreach (DataBinding<ModelData> model in unit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                _paths.Add(model, new List<Position>());
                _facingOffsets.Add(model, new List<float>());
            }
        }

        public bool ValidateAll(out List<ReasonForInvalidMove> invalidReasons)
        {
            List<ModelMoveEntry> resultsList = GetResultsAsList();
            return MovementUtilities.ValidatePaths(resultsList, _maxDistanceInches, out invalidReasons);
        }

        public void AddStep(IModel model, Position nextStep, float facingOffsetRadians = 0f)
        {
            DataBinding<ModelData> modelData = _paths.Keys.First(m => m.GetValue() == model);

            MovementUtilities.AssertModelInUnit(_unit, modelData);

            _paths[modelData].Add(nextStep);
            _facingOffsets[modelData].Add(facingOffsetRadians);
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
            _facingOffsets[modelData].RemoveAt(_facingOffsets[modelData].Count - 1);
        }

        public void ClearModelSteps(IModel model)
        {
            DataBinding<ModelData> modelData = _paths.Keys.First(m => m.GetValue() == model);


            MovementUtilities.AssertModelInUnit(_unit, modelData);

            _paths[modelData].Clear();
            _facingOffsets[modelData].Clear();
        }

        public void ClearAllSteps()
        {
            foreach (List<Position> path in _paths.Values)
            {
                path.Clear();
            }
            foreach (List<float> offsets in _facingOffsets.Values)
            {
                offsets.Clear();
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

        /// <summary>
        /// The facing offset each of this model's committed waypoints was placed with (#282), 1:1 with its path.
        /// </summary>
        public IReadOnlyList<float> GetModelFacingOffsets(IModel model)
        {
            return _facingOffsets.First(kvp => kvp.Key.GetValue() == model).Value;
        }

        /// <summary>
        /// Builds the per-model move entries. With a facing derivation (#150/#283), each entry carries a
        /// per-waypoint facing built from the manual offset that waypoint was placed with (#282 - captured by
        /// AddStep, so a late rotation never re-orients earlier waypoints): TravelDirection faces the travel
        /// into each waypoint (movement), RotateInPlace keeps the model's current facing rotated by each
        /// offset (consolidation). None (the default) leaves validation and non-facing callers unchanged.
        /// </summary>
        public List<ModelMoveEntry> GetResultsAsList(EPathFacingDerivation facingDerivation = EPathFacingDerivation.None)
        {
            List<ModelMoveEntry> results = new List<ModelMoveEntry>(_paths.Count);
            foreach(KeyValuePair< DataBinding<ModelData>, List<Position>> kvp in _paths)
            {
                List<Float2>? facings = null;
                if (facingDerivation != EPathFacingDerivation.None && kvp.Value.Count > 0)
                {
                    IModel model = kvp.Key.GetValue();
                    facings = facingDerivation == EPathFacingDerivation.TravelDirection
                        ? MovementFacingUtilities.WaypointFacings(model.Position, kvp.Value, model.Facing,
                            _facingOffsets[kvp.Key])
                        : MovementFacingUtilities.RotateInPlaceFacings(model.Facing, _facingOffsets[kvp.Key]);
                }
                results.Add(new ModelMoveEntry(kvp.Key, kvp.Value, facings));
            }

            return results;
        }
    }

}
