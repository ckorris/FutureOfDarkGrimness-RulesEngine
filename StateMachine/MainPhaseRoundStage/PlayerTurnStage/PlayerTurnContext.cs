

namespace FDG.Stages
{

    public interface IPlayerTurnContext : IGameContextAccessor
    {
        public IUnit ActivatedUnit { get; }

        public void ChooseUnitToActivate(IUnit unitToActivate);
    }

    public class PlayerTurnContext : IPlayerTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public IUnit ActivatedUnit { get; private set; }

        public PlayerTurnContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void ChooseUnitToActivate(IUnit unitToActivate)
        {
            ActivatedUnit = unitToActivate;
        }
    }
}