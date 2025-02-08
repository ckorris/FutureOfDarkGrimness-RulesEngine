using System;
using FDG.Data;

namespace FDG.Network.Synchronization
{
    internal class GameDataSynchronizer
    {

        private IReadWriteableGameDataStore _gameDataStore;

        public GameDataSynchronizer(IReadWriteableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;

            _gameDataStore.OnDataAddedUntyped += SendDataAddedMessageToAll;
            _gameDataStore.OnDataUpdatedUntyped += SendDataUpdatedMessageToAll;
            _gameDataStore.OnDataRemovedUntyped += SendDataRemovedMessageToAll;

            //Need to pass in network message thing to subscribe to messages received,
            //and player slots to subscribe to new players.
            //BUUUUUT actually we don't ever need all these in one place, do we? The host doesn't
            //need to receive catch-up messages and clients don't need to send things to new players.
            //Consider splitting or having a config, but that may be too much complexity for little savings.
        }

        private void SendDataAddedMessageToAll(DataReference data, Type type, object newObject)
        {
            throw new NotImplementedException();
        }

        private void SendDataUpdatedMessageToAll(DataReference data, Type type, object newValue)
        {
            throw new NotImplementedException();
        }

        private void SendDataRemovedMessageToAll(DataReference data, Type type, object removedObject)
        {
            throw new NotImplementedException();
        }

        private void SendAllDataToNewPlayer(PlayerID playerID)
        {
            throw new NotImplementedException();
        }

        private void OnReceivedDataAddedMessage() //TODO: Add params.
        {
            throw new NotImplementedException();
        }

        private void OnReceivedDataUpdatedMessage()
        {
            throw new NotImplementedException();
        }

        private void OnReceivedDataRemovedMessage()
        {
            throw new NotImplementedException();
        }

        private void OnReceivedAllDataMessage()
        {
            throw new NotImplementedException();
        }
    }
}
