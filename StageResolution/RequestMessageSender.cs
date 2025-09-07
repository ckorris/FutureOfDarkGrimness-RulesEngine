using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using Newtonsoft.Json;

namespace FDG.StageResolution
{
    internal class RequestMessageSender :  IPlayerRequestByID,  IDisposable
    {
        private IMessageBusHost _messageBusHost;
        private IReadableGameDataStore _gameDataStore;

        private Dictionary<TaskID, SuccessAndFailActions> _pendingTaskAndResolvers = new Dictionary<TaskID, SuccessAndFailActions>();

        public RequestMessageSender(IMessageBusHost messageBusHost, IReadableGameDataStore gameDataStore)
        {
            //_targetPlayerID = targetPlayerID;
            _messageBusHost = messageBusHost;
            _gameDataStore = gameDataStore;

            _messageBusHost.RegisterForMessageEvent<StageTaskReplyMessage>(OnReceivedReplyMessage);
            _messageBusHost.RegisterForMessageEvent<StageTaskRequestErrorMessage>(OnReceivedErrorMessage);
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) 
            where TRequest : IStageTaskRequest<TReply>
        {

            PlayerID targetPlayerID = request.TargetPlayerID;

            TaskID taskID = new TaskID(Guid.NewGuid());
            string? requestFullTypeName = typeof(TRequest).FullName;
            string? replyFullTypeName = typeof(TReply).FullName;

            if (requestFullTypeName == null)
            {
                throw new InvalidOperationException($"Type {typeof(TRequest)} has no full type name.");
            }

            if (replyFullTypeName == null)
            {
                throw new InvalidOperationException($"Type {typeof(TReply)} has no full type name.");
            }

            string? requestJson = JsonConvert.SerializeObject(request, _gameDataStore.GetJsonSettings());

            if (requestJson == null)
            {
                throw new JsonSerializationException($"Failed to serialize request of type {typeof(TRequest)}.");
            }

            StageTaskRequestMessage requestMessage = new StageTaskRequestMessage(targetPlayerID, taskID, requestFullTypeName,
                replyFullTypeName, requestJson);

            TaskCompletionSource<TReply> taskCompletionSource = new TaskCompletionSource<TReply>();

            Action<string> onSuccess = (replyJson) => DeserializeAndReturnReply(replyJson, taskCompletionSource);
            Action<string> onFailed = (replyJson) => ReturnFailure(targetPlayerID, requestFullTypeName,
                replyJson, taskCompletionSource);

            SuccessAndFailActions actions = new SuccessAndFailActions(onSuccess, onFailed);

            _pendingTaskAndResolvers.Add(taskID, actions);

            _messageBusHost.SendCommandToAllAsync(requestMessage);

            return taskCompletionSource.Task;
        }

        private void OnReceivedReplyMessage(StageTaskReplyMessage replyMessage)
        {
            /*
            if (replyMessage.PlayerID != _targetPlayerID)
            {
                return;
                //Ignore this message, and it's okay, all instances of this are listening.
            }
            */

            if (_pendingTaskAndResolvers.TryGetValue(replyMessage.TaskID, out SuccessAndFailActions? actions) == false)
            {
                throw new ArgumentException($"Received message trying to resolve task with ID that was not pending. Task ID: {replyMessage.TaskID} " +
                    $"Player ID: {replyMessage.PlayerID}");
            }

            if(actions == null)
            {
                throw new NullReferenceException($"Missing instance of {nameof(SuccessAndFailActions)} for task ID {replyMessage.TaskID}.");
            }

            actions.OnSuccessful.Invoke(replyMessage.ReplyJson);
            _pendingTaskAndResolvers.Remove(replyMessage.TaskID);
        }

        private void OnReceivedErrorMessage(StageTaskRequestErrorMessage errorMessage)
        {
            /*
            if (errorMessage.PlayerID != _targetPlayerID)
            {
                return;
                //Ignore this message, and it's okay, all instances of this are listening.
            }
            */

            if (_pendingTaskAndResolvers.TryGetValue(errorMessage.TaskID, out SuccessAndFailActions? actions) == false)
            {
                throw new ArgumentException($"Received error message trying to resolve task with ID that was not pending. Task ID: {errorMessage.TaskID} " +
                    $"Player ID: {errorMessage.PlayerID}");
            }

            actions.OnFailed.Invoke(errorMessage.ErrorMessage);
            _pendingTaskAndResolvers.Remove(errorMessage.TaskID);
        }

        private void DeserializeAndReturnReply<TReply>(string replyJson, TaskCompletionSource<TReply> replyTask)
        {
            TReply? reply = JsonConvert.DeserializeObject<TReply>(replyJson, _gameDataStore.GetJsonSettings());

            if (reply == null)
            {
                throw new JsonSerializationException($"Failed to deserialize reply from Json.");
            }

            replyTask.SetResult(reply);
        }

        private void ReturnFailure<TReply>(PlayerID playerID, string requestType, string errorMessage, TaskCompletionSource<TReply> replyTask)
        {
            replyTask.SetException(new NetworkedRequestFailedException(playerID, requestType, errorMessage));
        }

        public void Dispose()
        {
            _messageBusHost.DeregisterForMessageEvent<StageTaskReplyMessage>(OnReceivedReplyMessage);
            _messageBusHost.DeregisterForMessageEvent<StageTaskRequestErrorMessage>(OnReceivedErrorMessage);
        }



        private class SuccessAndFailActions
        {
            public readonly Action<string> OnSuccessful; //Call with Json
            public readonly Action<string> OnFailed; //Call with error message.

            public SuccessAndFailActions(Action<string> onSuccessful, Action<string> onFailed)
            {
                OnSuccessful = onSuccessful;
                OnFailed = onFailed;
            }
        }

        public class NetworkedRequestFailedException : Exception
        {
            public NetworkedRequestFailedException(PlayerID playerID, string requestType, string errorMessage)
                : base($"Remove client returned an error after receiving request of type {requestType}: {errorMessage}. " +
                      $"Player ID: {playerID}.")
            { }
        }
    }
}