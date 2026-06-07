
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public interface IMovementActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> MovingUnit { get; }

        public float MaxAdvanceDistance { get; }

        public float MaxRushDistance { get; }

        public float MaxChargeDistance { get; }

        public List<ITerrain> RelevantTerrain { get; }

        public bool TryGetMovementDistance(out float distance);

        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths);

        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths);
    }

    public class MovementActionContext : IMovementActionContext
    {
        public IGameContext GameContext { get; }


        public DataBinding<UnitData> MovingUnit { get; private set; }

        public float MaxAdvanceDistance
        {
            get
            {
                return _canMove ? _maxAdvanceDistance : 0f;
            }
        }

        public float MaxRushDistance
        {
            get
            {
                return _canMove ? _maxRushDistance : 0f;
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
        private float _maxRushDistance;
        private float _maxChargeDistance;

        private bool _hasMoved = false;
        private float? _movementDistance;
        private List<ModelMoveEntry> _paths;


        public MovementActionContext(IGameContext gameContext, DataBinding<UnitData> movingUnit)
        {
            GameContext = gameContext;

            MovingUnit = movingUnit;

            MovementContextPrecursor precursor = MovementContextPrecursor.GetDefault(gameContext);

            List<ISpecialRule_Movement> movementSpecialRules = movingUnit.GetValue().GetMovementSpecialRules();

            foreach(ISpecialRule_Movement movementSpecialRule in movementSpecialRules)
            {
                movementSpecialRule.ProcessMovementContextPrecursor(ref precursor);
            }

            _canMove = precursor.CanMove;
            _maxAdvanceDistance = precursor.MaxAdvanceDistance;
            _maxRushDistance = precursor.MaxRushDistance;
            _maxChargeDistance = precursor.MaxChargeDistance;
            RelevantTerrain = precursor.RelevantTerrain;
            
            //Movement special rules.
            IUnit unit = movingUnit.GetValue();
            MovementModifierSink movementModifiers = new MovementModifierSink();
            
            AccumulateMovementRules(unit, EActionType.Advance, _maxAdvanceDistance, movementModifiers);
            AccumulateMovementRules(unit, EActionType.Rush, _maxRushDistance, movementModifiers);
            AccumulateMovementRules(unit, EActionType.Charge, _maxChargeDistance, movementModifiers);

            _maxAdvanceDistance += movementModifiers.Net(EActionType.Advance);
            _maxRushDistance += movementModifiers.Net(EActionType.Rush);
            _maxChargeDistance += movementModifiers.Net(EActionType.Charge);

        }
        
        //TODO: Move down.
        private void AccumulateMovementRules(IUnit unit, EActionType action, float baseDistance,
            MovementModifierSink sink)
        {
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new MoveActionDeclaredContext(unit, action, baseDistance),
                (unit, ERuleSeat.Actor));
            sink.ApplyFrom(operations);
        }

        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths)
        {
            _hasMoved = true;
            _movementDistance = MovementUtilities.GetMaxMoveDistance(paths);
            _paths = paths;
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


        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths)
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

        public float MaxRushDistance;

        public float MaxChargeDistance;

        public List<ITerrain> RelevantTerrain;

        public static MovementContextPrecursor GetDefault(IGameContext gameContext)
        {
            MovementContextPrecursor precursor = new MovementContextPrecursor()
            {
                CanMove = true,
                MaxAdvanceDistance = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES,
                MaxRushDistance = GameWideConstants.RUSH_DISTANCE_INCHES,
                MaxChargeDistance = GameWideConstants.CHARGE_DISTANCE_INCHES,
                RelevantTerrain = new List<ITerrain>(gameContext.TableState.Terrain.Objects)
            };

            return precursor;
        }
    }
}
