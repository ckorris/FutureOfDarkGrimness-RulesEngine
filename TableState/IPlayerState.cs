using System.Collections.Generic;

namespace FDG
{
    public interface IPlayerState
    {
        IReadOnlyCollection<PlayerInfo> Players { get; }
    }

    public class PlayerState : IPlayerState
    {
        public IReadOnlyCollection<PlayerInfo> Players => _players;

        private HashSet<PlayerInfo> _players;

        public PlayerState()
        {
            _players = new HashSet<PlayerInfo>();
        }

        public PlayerState(HashSet<PlayerInfo> players)
        {
            _players = players;
        }
    }
}
