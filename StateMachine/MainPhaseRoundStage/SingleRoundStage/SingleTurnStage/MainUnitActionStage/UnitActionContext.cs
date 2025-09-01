
using FDG.Data;

namespace FDG.Stages
{

    public interface IUnitActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> ActivatingUnit { get; }

        public bool HasMoved { get; }

        public float MoveDistance { get;}

        public bool HasAttacked { get; }

        public void RegisterMoveFinished(float distance);

        public void RegisterAttackedFinished();

        public void Reset(DataBinding<UnitData> activatingUnit);
    }

    public class UnitActionContext : IUnitActionContext
    {
        public IGameContext GameContext { get; private set; }

        public DataBinding<UnitData> ActivatingUnit { get; private set; }

        public bool HasMoved { get; private set; }

        public float MoveDistance { get; private set; } = 0f;

        public bool HasAttacked { get; private set; }


        public UnitActionContext(IGameContext gameContext, DataBinding<UnitData> activatingUnit)
        {
            GameContext = gameContext;
            ActivatingUnit = activatingUnit;
        }

        public void RegisterMoveFinished(float distance)
        {
            HasMoved = true;
            MoveDistance = distance;
        }

        public void RegisterAttackedFinished()
        {
            HasAttacked = true;
        }

        //TODO: This pattern sucks, make a new instance of the context each time.
        public void Reset(DataBinding<UnitData> activatingUnit) 
        {
            ActivatingUnit = activatingUnit;
            HasMoved = false;
            MoveDistance = 0f;
            HasAttacked = false;
        }
    }

    public static class IUnitActionContextExtensions
    {
        public static PlayerID ActivatingPlayer(this IUnitActionContext context)
        {
            return context.ActivatingUnit.GetValue().PlayerID;
        }
    }
}