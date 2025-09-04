using FDG.Data;
using FDG.Data.Containers;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;

namespace FDG.Network.Synchronization
{
    internal class GameDataUpdateSender
    {

        private IReadWriteableGameDataStore _gameDataStore;

        private ICommandDispatcher _commandDispatcher;

        public GameDataUpdateSender(IReadWriteableGameDataStore gameDataStore, ICommandDispatcher commandDispatcher)
        {
            _gameDataStore = gameDataStore;
            _commandDispatcher = commandDispatcher;

            _gameDataStore.OnDataAddedAsJson += SendDataAddedMessageToAll;
            _gameDataStore.OnDataUpdatedAsJson += SendDataUpdatedMessageToAll;
            _gameDataStore.OnDataRemoved += SendDataRemovedMessageToAll;

            _commandDispatcher.RegisterForMessageEvent<RequestAllDataMessage>(OnReceivedRequestAllDataMessage);
        }

        private void SendDataAddedMessageToAll(DataReference data, string newObjectJson)
        {
            AddSingleDataMessage addMessage = new AddSingleDataMessage(data, newObjectJson);
            _commandDispatcher.SendCommandToAllAsync(addMessage);
        }

        private void SendDataUpdatedMessageToAll(DataReference data, string newValueJson)
        {
            UpdateSingleDataMessage updateMessage = new UpdateSingleDataMessage(data, newValueJson);
            _commandDispatcher.SendCommandToAllAsync(updateMessage);
        }

        private void SendDataRemovedMessageToAll(DataReference data)
        {
            RemoveSingleDataMessage removeMessage = new RemoveSingleDataMessage(data);
            _commandDispatcher.SendCommandToAllAsync(removeMessage);
        }

        private void OnReceivedRequestAllDataMessage(RequestAllDataMessage _, ConnectionID connectionID)
        {
            List<ReferenceJsonValuePair> allData = _gameDataStore.GetAllDataReferencesAsJson();
            AddAllDataMessage allDataMessage = new AddAllDataMessage(allData);
            _commandDispatcher.SendCommandToSingleAsync(allDataMessage, connectionID);
        }



    }
}
