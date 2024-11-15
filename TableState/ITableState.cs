

namespace FDG
{
    public interface ITableState
    {
        public IPlayerState PlayerState { get; }

        public IArmyState ArmyState { get; }
    }

    public class TableState : ITableState
    {
        public IPlayerState PlayerState { get; private set; }

        public IArmyState ArmyState { get; private set; }

        public TableState()
        {
            PlayerState = new PlayerState();

            ArmyState = new ArmyState();
        }

        public TableState(IPlayerState playerState, IArmyState armyState)
        {
            PlayerState = playerState;
            ArmyState = armyState;
        }
    }
}
