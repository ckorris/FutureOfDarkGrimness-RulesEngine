using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;

namespace FDG.StageResolution
{
    internal class NetworkedRequestMessageReceiver : IDisposable //TODO: Doesn't need to be named "Networked" anymore.
    {
        private PlayerID _playerID;
        private IMessageBusClient _messageBusClient;
        private IStageResolverRegistry _stageResolverRegistry;
        private OutstandingTaskLister _outstandingTaskLister;
        private IReadableGameDataStore _gameDataStore;


        public NetworkedRequestMessageReceiver(PlayerID playerID, IMessageBusClient messageBusClient, 
            IStageResolverRegistry stageResolverRegistry, OutstandingTaskLister outstandingTaskLister,
            IReadableGameDataStore gameDataStore)
        {
            _playerID = playerID;
            _messageBusClient = messageBusClient;
            _stageResolverRegistry = stageResolverRegistry;
            _outstandingTaskLister = outstandingTaskLister;
            _gameDataStore = gameDataStore;

            _messageBusClient.RegisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnReceivedNotifyAwaitingMessage);
            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnReceivedNotifyResolvedMessage);
        }

        private void OnReceivedStageTaskRequestMessage(StageTaskRequestMessage requestMessage)
        {
            if(requestMessage.PlayerID != _playerID)
            {
                string errorString = $"Received a message targeting the wrong player. This ID: {_playerID}. " + 
                    $"Target: {requestMessage.PlayerID}. Request type: {requestMessage.RequestFullTypeName} Request ID: {requestMessage.TaskID}.";

                StageTaskRequestErrorMessage errorMessage =
                    new StageTaskRequestErrorMessage(requestMessage.PlayerID, requestMessage.TaskID, errorString);
                _messageBusClient.SendCommandToHostAsync(errorMessage);

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

        private void OnReceivedNotifyAwaitingMessage(StageTaskNotifyAwaitingMessage awaitingMessage)
        {
            _outstandingTaskLister.NotifyTaskRequested(awaitingMessage.PlayerID, awaitingMessage.TaskID, 
                awaitingMessage.UserFriendlyTaskName);
        }

        private void OnReceivedNotifyResolvedMessage(StageTaskNotifyResolvedMessage resolvedMessage)
        {
            _outstandingTaskLister.NotifyTaskResolved(resolvedMessage.TaskID);
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
            _messageBusClient.DeregisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnReceivedNotifyAwaitingMessage);
            _messageBusClient.DeregisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnReceivedNotifyResolvedMessage);
        }
    }
}