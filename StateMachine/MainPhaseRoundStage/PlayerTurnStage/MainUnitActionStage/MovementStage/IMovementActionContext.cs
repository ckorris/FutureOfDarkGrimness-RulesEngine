
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public interface IMovementActionContext
    {
        public IUnit MovingUnit { get; }

        public float MaxAdvanceDistance { get; }

        public float MaxChargeDistance { get; }

        public List<ITerrain> RelevantTerrain { get; }

        public bool TryGetMovementDistance(out float distance);

        public bool TryGetPaths(out IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths);

        public void SubmitValidPathTemplate(PathTemplate pathTemplate);
    }

    public class MovementActionContext : IMovementActionContext
    {
        public IUnit MovingUnit { get; private set; }

        public float MaxAdvanceDistance
        {
            get
            {
                return _canMove ? _maxAdvanceDistance : 0f;
            }
        }

        public float MaxChargeDistance
        {
            get
            {
                return _canMove ? _maxChargeDistance : 0f;
            }
        }

        public List<ITerrain> RelevantTerrain { get; private set; }

        private bool _canMove;
        private float _maxAdvanceDistance;
        private float _maxChargeDistance;

        private bool _hasMoved = false;
        private float? _movementDistance;
        private IReadOnlyDictionary<IModel, IReadOnlyList<Position>> _paths;

        public MovementActionContext(IGameContext gameContext, IUnit movingUnit)
        {
            MovingUnit = movingUnit;

            MovementContextPrecursor precursor = MovementContextPrecursor.GetDefault(gameContext);

            List<ISpecialRule_Movement> movementSpecialRules = movingUnit.GetMovementSpecialRules();

            foreach(ISpecialRule_Movement movementSpecialRule in movementSpecialRules)
            {
                movementSpecialRule.ProcessMovementContextPrecursor(ref precursor);
            }

            _canMove = precursor.CanMove;
            _maxAdvanceDistance = precursor.MaxAdvanceDistance;
            _maxChargeDistance = precursor.MaxChargeDistance;
            RelevantTerrain = precursor.RelevantTerrain;
        }

        public void SubmitValidPathTemplate(PathTemplate pathTemplate)
        {
            if (pathTemplate.ValidatePaths(out List<ReasonForInvalidMove> errorReasons) == false)
            {
                throw new InvalidOperationException($"Defined invalid path to {nameof(DefinePathStage)}. Contained {errorReasons.Count} errors. " +
                    $"You can call {nameof(PathTemplate)}.{nameof(PathTemplate.ValidatePaths)} before listing the path as valid.");
            }

            _hasMoved = true;
            _movementDistance = pathTemplate.GetMaxMoveDistance();
            _paths = pathTemplate.CurrentPaths;
        }

        public bool TryGetMovementDistance(out float distance)
        {
            if(_hasMoved)
            {
                distance = _movementDistance.Value;
                return true;
            }

            distance = float.NegativeInfinity;
            return false;
        }


        public bool TryGetPaths(out IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths)
        {
            if(_hasMoved)
            {
                paths = _paths;
                return true;
            }

            paths = null;
            return false;
        }
    }

    public struct MovementContextPrecursor
    {
        public bool CanMove;

        public float MaxAdvanceDistance;

        public float MaxChargeDistance;

        public List<ITerrain> RelevantTerrain;

        public static MovementContextPrecursor GetDefault(IGameContext gameContext)
        {
            MovementContextPrecursor precursor = new MovementContextPrecursor()
            {
                CanMove = true,
                MaxAdvanceDistance = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES,
                MaxChargeDistance = GameWideConstants.CHARGE_DISTANCE_INCHES,
                RelevantTerrain = new List<ITerrain>(gameContext.TableState.TerrainState.Terrain)
            };

            return precursor;
        }
    }
}
