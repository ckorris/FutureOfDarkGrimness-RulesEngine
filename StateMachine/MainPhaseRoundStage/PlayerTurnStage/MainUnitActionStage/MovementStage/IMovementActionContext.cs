
using System.Collections.Generic;

namespace FDG.Stages
{
    public interface IMovementActionContext
    {
        public IUnit MovingUnit { get; }

        public float MaxAdvanceDistance { get; }

        public float MaxChargeDistance { get; }

        public List<ITerrain> RelevantTerrain { get; }

        public float GetMaxDistanceMoved();
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

        public MovementActionContext(GameContext gameContext, IUnit movingUnit)
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

        public float GetMaxDistanceMoved()
        {
            throw new System.NotImplementedException();
        }
    }

    public struct MovementContextPrecursor
    {
        public bool CanMove;

        public float MaxAdvanceDistance;

        public float MaxChargeDistance;

        public List<ITerrain> RelevantTerrain;

        public static MovementContextPrecursor GetDefault(GameContext gameContext)
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
