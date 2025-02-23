using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;
using FutureOfDarkGrimness.Network.Messages.StageRequestMessages;

namespace FDG.StageResolution
{
    internal class NetworkedRequestMessageReceiver : IDisposable
    {
        private PlayerID _playerID;
        private ICommandDispatcher _commandDispatcher;
        private StageResolverRegistry _stageResolverRegistry;
        private OutstandingTaskLister _outstandingTaskLister;
        private IReadableGameDataStore _gameDataStore;


        public NetworkedRequestMessageReceiver(PlayerID playerID, ICommandDispatcher commandDispatcher, 
            StageResolverRegistry stageResolverRegistry, OutstandingTaskLister outstandingTaskLister,
            IReadableGameDataStore gameDataStore)
        {
            _playerID = playerID;
            _commandDispatcher = commandDispatcher;
            _stageResolverRegistry = stageResolverRegistry;
            _outstandingTaskLister = outstandingTaskLister;
            _gameDataStore = gameDataStore;

            _commandDispatcher.RegisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
            _commandDispatcher.RegisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnReceivedNotifyAwaitingMessage);
            _commandDispatcher.RegisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnReceivedNotifyResolvedMessage);
        }

        private void OnReceivedStageTaskRequestMessage(StageTaskRequestMessage requestMessage, ConnectionID sourceConnectionID)
        {

            _ = HandleRequestMessageAsync(requestMessage, sourceConnectionID);
        }

        private async Task HandleRequestMessageAsync(StageTaskRequestMessage requestMessage, ConnectionID sourceConnectionID)
        {
            try
            {
                Task<string> replyJson = _stageResolverRegistry.ResolveRequestAsJson(requestMessage.RequestFullTypeName,
                    requestMessage.RequestJson, _gameDataStore);

                await replyJson;

                StageTaskReplyMessage replyMessage = new StageTaskReplyMessage(requestMessage.PlayerID, requestMessage.TaskID,
                    requestMessage.ReplyFullTypeName, replyJson.Result);

                await _commandDispatcher.SendCommandAsync(replyMessage, sourceConnectionID);
            }
            catch (Exception ex)
            {
                StageTaskRequestErrorMessage errorMessage = 
                    new StageTaskRequestErrorMessage(requestMessage.PlayerID, requestMessage.TaskID, ex.ToString());
            }
        }

        private void OnReceivedNotifyAwaitingMessage(StageTaskNotifyAwaitingMessage awaitingMessage, 
            ConnectionID sourceConnectionID)
        {
            _outstandingTaskLister.NotifyTaskRequested(awaitingMessage.PlayerID, awaitingMessage.TaskID, 
                awaitingMessage.UserFriendlyTaskName);
        }

        private void OnReceivedNotifyResolvedMessage(StageTaskNotifyResolvedMessage resolvedMessage, 
            ConnectionID sourceConnectionID)
        {
            _outstandingTaskLister.NotifyTaskResolved(resolvedMessage.TaskID);
        }

        public void Dispose()
        {
            _commandDispatcher.DeregisterForMessageEvent<StageTaskRequestMessage>(OnReceivedStageTaskRequestMessage);
            _commandDispatcher.DeregisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnReceivedNotifyAwaitingMessage);
            _commandDispatcher.DeregisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnReceivedNotifyResolvedMessage);
        }
    }
}
