using FDG.Data;
using FDG.Data.Containers;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.DataMessages;
using FDG.SaveLoad;

namespace FDG.Network.Synchronization
{
    /// <summary>
    /// A networked client's side of the store sync: applies the host's incremental deltas through
    /// <see cref="StoreDeltaApplier"/> (shared with replay playback, #454) and rebuilds the whole store
    /// from the join-time catch-up through <see cref="StoreReplay"/>.
    /// </summary>
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

        private void OnReceivedDataAddedMessage(AddSingleDataMessage addMessage)
        {
            StoreDeltaApplier.ApplyAdd(_gameDataStore, addMessage.DataReference, addMessage.InitialValueAsJson);
        }

        private void OnReceivedDataUpdatedMessage(UpdateSingleDataMessage updateMessage)
        {
            StoreDeltaApplier.ApplyUpdate(_gameDataStore, updateMessage.DataReference, updateMessage.ValueAsJson);
        }

        private void OnReceivedDataRemovedMessage(RemoveSingleDataMessage removeMessage)
        {
            StoreDeltaApplier.ApplyRemove(_gameDataStore, removeMessage.DataReference);
        }

        private void OnReceivedAllDataMessage(AddAllDataMessage allDataMessage)
        {
            // The catch-up snapshot lists entries in store (registration) order, which is not dependency
            // order - e.g. ModelData carries a DataBinding<Float2> facing but Float2 is registered last
            // (#150). Replaying with the shared StoreReplay pipeline retries forward references, rehydrates
            // [JsonIgnore] rule blobs (#095) and rewires wound subscriptions - identical to save/load, so a
            // networked client ends up with the same populated, rule-carrying store the host has. A naive
            // single pass instead threw IsNotAssigned on the first forward reference and left the client
            // store empty, which then failed the first deploy SelectionRequest<UnitData>.
            StoreReplay.Rebuild(_gameDataStore, allDataMessage.AllData);
        }
    }
}
