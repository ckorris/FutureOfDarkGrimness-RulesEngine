

using FDG.Data;

namespace FDG.Stages
{

    public interface IPlayerTurnContext : IGameContextAccessor
    {
        public PlayerID? ActivatedPlayer { get; }

        public DataBinding<UnitData> ActivatedUnit { get; }

        public void SetActivatedPlayer(PlayerID playerID);

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate);
    }

    public class PlayerTurnContext : IPlayerTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public DataBinding<UnitData> ActivatedUnit { get; private set; }

        public PlayerID? ActivatedPlayer { get; private set; }

        public PlayerTurnContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void SetActivatedPlayer(PlayerID playerID)
        {
            ActivatedPlayer = playerID;
        }

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate)
        {
            ActivatedUnit = unitToActivate;
        }


    }
}