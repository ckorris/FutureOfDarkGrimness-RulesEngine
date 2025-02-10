using FDG.Data;
using FDG.Data.Containers;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;

namespace FDG.Network.Synchronization
{
    internal class GameDataSynchronizer
    {

        private IReadWriteableGameDataStore _gameDataStore;

        private ICommandDispatcher _commandDispatcher;

        public GameDataSynchronizer(IReadWriteableGameDataStore gameDataStore, ICommandDispatcher commandDispatcher)
        {
            _gameDataStore = gameDataStore;
            _commandDispatcher = commandDispatcher;

            _gameDataStore.OnDataAddedAsJson += SendDataAddedMessageToAll;
            _gameDataStore.OnDataUpdatedAsJson += SendDataUpdatedMessageToAll;
            _gameDataStore.OnDataRemoved += SendDataRemovedMessageToAll;

            _commandDispatcher.RegisterForMessageEvent<AddSingleDataMessage>(OnReceivedDataAddedMessage);
            _commandDispatcher.RegisterForMessageEvent<UpdateSingleDataMessage>(OnReceivedDataUpdatedMessage);
            _commandDispatcher.RegisterForMessageEvent<RemoveSingleDataMessage>(OnReceivedDataRemovedMessage);
            _commandDispatcher.RegisterForMessageEvent<AddAllDataMessage>(OnReceivedAllDataMessage);
            _commandDispatcher.RegisterForMessageEvent<RequestAllDataMessage>(OnReceivedRequestAllDataMessage);

            //Need to pass in network message thing to subscribe to messages received,
            //and player slots to subscribe to new players.
            //BUUUUUT actually we don't ever need all these in one place, do we? The host doesn't
            //need to receive catch-up messages and clients don't need to send things to new players.
            //Consider splitting or having a config, but that may be too much complexity for little savings.
        }

        /// <summary>
        /// Used to catch up when first joining a session.
        /// </summary>
        public void RequestAllCurrentData()
        {
            _commandDispatcher.SendCommandAsync(new RequestAllDataMessage());
        }


        private void SendDataAddedMessageToAll(DataReference data, string newObjectJson)
        {
            AddSingleDataMessage addMessage = new AddSingleDataMessage(data, newObjectJson);
            _commandDispatcher.SendCommandAsync(addMessage);
        }

        private void SendDataUpdatedMessageToAll(DataReference data, string newValueJson)
        {
            UpdateSingleDataMessage updateMessage = new UpdateSingleDataMessage(data, newValueJson);
            _commandDispatcher.SendCommandAsync(updateMessage);
        }

        private void SendDataRemovedMessageToAll(DataReference data)
        {
            RemoveSingleDataMessage removeMessage = new RemoveSingleDataMessage(data);
            _commandDispatcher.SendCommandAsync(removeMessage);
        }

        private void OnReceivedRequestAllDataMessage(RequestAllDataMessage _, ConnectionID connectionID)
        {
            List<ReferenceJsonValuePair> allData = _gameDataStore.GetAllDataReferencesAsJson();
            AddAllDataMessage allDataMessage = new AddAllDataMessage(allData);
            _commandDispatcher.SendCommandAsync(allDataMessage, connectionID);
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
            foreach(ReferenceJsonValuePair refValuePair in allDataMessage.AllData)
            {
                _gameDataStore.CreateFromReferenceAndJson(refValuePair.DataReference, refValuePair.JsonValue);
            }
        }

    }
}
