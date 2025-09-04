using FDG.Data;
using FDG.Data.Containers;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;

namespace FDG.Network.Synchronization
{
    internal class GameDataUpdateReceiver
    {
        private IReadWriteableGameDataStore _gameDataStore;

        private IMessageBusClient _messageBusClient;

        public GameDataUpdateReceiver(IReadWriteableGameDataStore gameDataStore, IMessageBusClient messageBusClient)
        {
            _gameDataStore = gameDataStore;
            _messageBusClient = messageBusClient;

            _messageBusClient.RegisterForMessageEvent<AddSingleDataMessage>(OnReceivedDataAddedMessage);
            _messageBusClient.RegisterForMessageEvent<UpdateSingleDataMessage>(OnReceivedDataUpdatedMessage);
            _messageBusClient.RegisterForMessageEvent<RemoveSingleDataMessage>(OnReceivedDataRemovedMessage);
            _messageBusClient.RegisterForMessageEvent<AddAllDataMessage>(OnReceivedAllDataMessage);

        }

        /// <summary>
        /// Used to catch up when first joining a session.
        /// </summary>
        public void RequestAllCurrentData()
        {
            _messageBusClient.SendCommandToHostAsync(new RequestAllDataMessage());
        }

        private void OnReceivedDataAddedMessage(AddSingleDataMessage addMessage, ConnectionID _)
        {
            _gameDataStore.CreateFromReferenceAndJson(addMessage.DataReference, addMessage.InitialValueAsJson);
        }

        private void OnReceivedDataUpdatedMessage(UpdateSingleDataMessage updateMessage, ConnectionID _)
        {
            _gameDataStore.SetValueWithJson(updateMessage.DataReference, updateMessage.ValueAsJson);
        }

        private void OnReceivedDataRemovedMessage(RemoveSingleDataMessage removeMessage, ConnectionID _)
        {
            _gameDataStore.Destroy(removeMessage.DataReference);
        }

        private void OnReceivedAllDataMessage(AddAllDataMessage allDataMessage, ConnectionID _)
        {
            foreach (ReferenceJsonValuePair refValuePair in allDataMessage.AllData)
            {
                _gameDataStore.CreateFromReferenceAndJson(refValuePair.DataReference, refValuePair.JsonValue);
            }
        }
    }
}
