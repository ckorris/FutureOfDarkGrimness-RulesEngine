
namespace FDG
{
    public interface IArmyState
    {
        public IReadOnlyDictionary<PlayerID, IArmy> PlayerArmies { get; }
    }

    public class ArmyState : IArmyState
    {
        public IReadOnlyDictionary<PlayerID, IArmy> PlayerArmies => _playerArmies;

        private Dictionary<PlayerID, IArmy> _playerArmies; //TODO: Make sure this serializes and deserializes correctly.

        public ArmyState()
        {
            _playerArmies = new Dictionary<PlayerID, IArmy>();
        }

        public ArmyState(Dictionary<PlayerID, IArmy> playerArmies)
        {
            _playerArmies = playerArmies;
        }

        //I don't think we need to modify this for things like adding new players/armies later.
    }
}