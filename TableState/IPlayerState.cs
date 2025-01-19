using FDG.Data;
using System.Collections.Generic;

namespace FDG
{
    public interface IPlayerState
    {
        IEnumerable<PlayerData> Players { get; }
    }

    public class PlayerState : IPlayerState
    {
        public IEnumerable<PlayerData> Players => _gameDataStore.GetAllValues<PlayerData>();

        private IReadableGameDataStore _gameDataStore;


        public PlayerState(IReadableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }
    }
}
