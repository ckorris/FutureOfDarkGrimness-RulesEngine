using FDG.Data;
using FDG.Data.Containers;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;

namespace FDG.Network.Synchronization
{
    internal class GameDataUpdateSender
    {

        private IReadWriteableGameDataStore _gameDataStore;

        private IMessageBusHost _messageBusHost;

        public GameDataUpdateSender(IReadWriteableGameDataStore gameDataStore, IMessageBusHost messageBusHost)
        {
            _gameDataStore = gameDataStore;
            _messageBusHost = messageBusHost;

            _gameDataStore.OnDataAddedAsJson += SendDataAddedMessageToAll;
            _gameDataStore.OnDataUpdatedAsJson += SendDataUpdatedMessageToAll;
            _gameDataStore.OnDataRemoved += SendDataRemovedMessageToAll;

            _messageBusHost.RegisterForMessageEvent<RequestAllDataMessage>(OnReceivedRequestAllDataMessage);
        }

        private void SendDataAddedMessageToAll(DataReference data, string newObjectJson)
        {
            AddSingleDataMessage addMessage = new AddSingleDataMessage(data, newObjectJson);
            _messageBusHost.SendCommandToAllAsync(addMessage);
        }

        private void SendDataUpdatedMessageToAll(DataReference data, string newValueJson)
        {
            UpdateSingleDataMessage updateMessage = new UpdateSingleDataMessage(data, newValueJson);
            _messageBusHost.SendCommandToAllAsync(updateMessage);
        }

        private void SendDataRemovedMessageToAll(DataReference data)
        {
            RemoveSingleDataMessage removeMessage = new RemoveSingleDataMessage(data);
            _messageBusHost.SendCommandToAllAsync(removeMessage);
        }

        private void OnReceivedRequestAllDataMessage(RequestAllDataMessage _)
        {
            List<ReferenceJsonValuePair> allData = _gameDataStore.GetAllDataReferencesAsJson();
            AddAllDataMessage allDataMessage = new AddAllDataMessage(allData);

            ConnectionID connectionID = _messageBusHost.GetCurrentMessageConnectionID();
            _messageBusHost.SendCommandToSingleAsync(allDataMessage, connectionID);
        }



    }
}
