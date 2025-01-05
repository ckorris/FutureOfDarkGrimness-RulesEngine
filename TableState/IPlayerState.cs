using FDG.Data;
using System.Collections.Generic;

namespace FDG
{
    public interface IPlayerState
    {
        IEnumerable<PlayerInfo> Players { get; }
    }

    public class PlayerState : IPlayerState
    {
        public IEnumerable<PlayerInfo> Players => _gameDataStore.GetAllValues<PlayerInfo>();

        private IReadableGameDataStore _gameDataStore;


        public PlayerState(IReadableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }
    }
}
