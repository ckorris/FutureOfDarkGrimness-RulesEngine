using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Messages.StageRequestMessages;
using System.Diagnostics;

namespace FDG.StageResolution
{
    internal class NetworkedRequestMessageReceiver : IDisposable //TODO: Doesn't need to be named "Networked" anymore.
    {
        private PlayerID _playerID;
        private IMessageBusClient _messageBusClient;
        private IStageResolverRegistry _stageResolverRegistry;
        private IReadableGameDataStore _gameDataStore;


        public NetworkedRequestMessageReceiver(PlayerID playerID, IMessageBusClient messageBusClient, 
            IStageResolverRegistry stageResolverRegistry, IReadableGameDataStore gameDataStore)
        {
            _playerID = playerID;
            _messageBusClient = messageBusClient;
            _stageResolverRegistry = stageResolverRegistry;
            _gameDataStore = gameDataStore;

            _messageBusClient.RegisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
        }

        private void OnReceivedStageTaskRequestMessage(StageTaskRequestMessage requestMessage)
        {
            Debug.WriteLine($"{nameof(NetworkedRequestMessageReceiver)} received {nameof(StageTaskRequestMessage)} of type: {requestMessage.RequestFullTypeName}.");

            if(requestMessage.PlayerID != _playerID)
            {
                return;
            }

            _ = HandleRequestMessageAsync(requestMessage);
        }

        private async Task HandleRequestMessageAsync(StageTaskRequestMessage requestMessage)
        {
            try
            {
                Task<string> replyJson = _stageResolverRegistry.ResolveRequestAsJson(requestMessage.RequestFullTypeName,
                    requestMessage.RequestJson, _gameDataStore);

                await replyJson;

                StageTaskReplyMessage replyMessage = new StageTaskReplyMessage(requestMessage.PlayerID, requestMessage.TaskID,
                    requestMessage.ReplyFullTypeName, replyJson.Result);

                await _messageBusClient.SendCommandToHostAsync(replyMessage);
            }
            catch (Exception ex)
            {
                StageTaskRequestErrorMessage errorMessage = 
                    new StageTaskRequestErrorMessage(requestMessage.PlayerID, requestMessage.TaskID, ex.ToString());
            }
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
        }
    }
}