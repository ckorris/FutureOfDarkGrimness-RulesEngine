
using FDG.Data;
using FDG.Rules.Foundation;

namespace FDG.Stages
{

    public interface IUnitActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> ActivatingUnit { get; }

        public bool HasMoved { get; }

        public float MoveDistance { get;}

        public bool HasAttacked { get; }

        /// <summary>
        /// Whether the unit was Shaken at the instant this activation began (snapshotted in
        /// <see cref="Reset"/>). This is the signal that decides Shaken recovery: a unit Shaken
        /// at activation start must idle this activation and recover, whereas a unit that becomes
        /// Shaken *during* its activation keeps the token for its next one. Captured explicitly so
        /// the recovery rule doesn't have to infer "activation start" from the action flags.
        /// </summary>
        public bool StartedActivationShaken { get; }

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

        public bool StartedActivationShaken { get; private set; }


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
            StartedActivationShaken = activatingUnit.GetValue().Tokens.HasToken(TokenType.Shaken);
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