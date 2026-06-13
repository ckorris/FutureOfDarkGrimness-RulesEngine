using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace FDG.StageResolution
{
    internal class RequestMessageSender :  IPlayerRequestByID,  IDisposable
    {
        private IMessageBusHost _messageBusHost;
        private IReadableGameDataStore _gameDataStore;
        private PlayerSlotManager _playerSlotManager;
        private ITextOutput _textOutput;

        // Mutated from both the engine thread (RequestDecision adds) and the bus/network read
        // thread (reply/error handlers remove) — must be concurrent-safe (#084).
        private readonly ConcurrentDictionary<TaskID, SuccessAndFailActions> _pendingTaskAndResolvers
            = new ConcurrentDictionary<TaskID, SuccessAndFailActions>();

        public RequestMessageSender(IMessageBusHost messageBusHost, IReadableGameDataStore gameDataStore,
            PlayerSlotManager playerSlotManager, ITextOutput textOutput)
        {
            _messageBusHost = messageBusHost;
            _gameDataStore = gameDataStore;
            _playerSlotManager = playerSlotManager;
            _textOutput = textOutput;

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

            // RunContinuationsAsynchronously so SetResult/SetException doesn't resume the awaiting
            // engine stage code synchronously on the network read loop (#084).
            TaskCompletionSource<TReply> taskCompletionSource =
                new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<string> onSuccess = (replyJson) => DeserializeAndReturnReply(replyJson, taskCompletionSource);
            Action<string> onFailed = (replyJson) => ReturnFailure(targetPlayerID, requestFullTypeName,
                replyJson, taskCompletionSource);

            SuccessAndFailActions actions = new SuccessAndFailActions(onSuccess, onFailed);

            _pendingTaskAndResolvers.TryAdd(taskID, actions);

            // Send the awaiting notification BEFORE the request message so OutstandingTaskLister
            // entries are populated before any synchronous resolver (e.g. AI) can produce a reply
            // and trigger the resolve notification — otherwise the resolve fires against an empty
            // lister dict and the entry that arrives next stays stuck forever.
            DataBinding<PlayerSlotInfo> playerInfoBinding =  _playerSlotManager.GetSlotByID(request.TargetPlayerID).InfoBinding;
            StageTaskNotifyAwaitingMessage awaitingMessage = new StageTaskNotifyAwaitingMessage(taskID, playerInfoBinding, request.TaskName);
            _messageBusHost.SendCommandToAllAsync(awaitingMessage);

            _messageBusHost.SendCommandToAllAsync(requestMessage);

            return taskCompletionSource.Task;
        }

        private void OnReceivedReplyMessage(StageTaskReplyMessage replyMessage)
        {
            // Atomically claim the task so a duplicate reply can't double-invoke the resolver.
            // An unknown/duplicate TaskID (stray or replayed reply) must NOT throw here: this runs
            // inside bus dispatch with no surrounding try/catch, so throwing would tear down the
            // connection. Log-and-ignore (idempotent) instead; only assert-throw in DEBUG so the
            // condition still surfaces during development. (#085)
            if (_pendingTaskAndResolvers.TryRemove(replyMessage.TaskID, out SuccessAndFailActions? actions) == false)
            {
                _textOutput.Log($"Ignoring reply for unknown or already-resolved task. Task ID: {replyMessage.TaskID} " +
                    $"Player ID: {replyMessage.PlayerID}");
#if DEBUG
                throw new ArgumentException($"Received message trying to resolve task with ID that was not pending. Task ID: {replyMessage.TaskID} " +
                    $"Player ID: {replyMessage.PlayerID}");
#else
                return;
#endif
            }

            if(actions == null)
            {
                throw new NullReferenceException($"Missing instance of {nameof(SuccessAndFailActions)} for task ID {replyMessage.TaskID}.");
            }

            //Notify all clients that the task is finished, so they can stop displaying it on the UI.
            StageTaskNotifyResolvedMessage finishedMessage = new StageTaskNotifyResolvedMessage(replyMessage.TaskID);
            _messageBusHost.SendCommandToAllAsync(finishedMessage);

            actions.OnSuccessful.Invoke(replyMessage.ReplyJson);
        }

        private void OnReceivedErrorMessage(StageTaskRequestErrorMessage errorMessage)
        {
            // Atomically claim the task so a duplicate error reply can't double-invoke the resolver.
            // As with replies, an unknown/duplicate TaskID must not throw inside bus dispatch (it
            // would kill the connection) — log-and-ignore, assert-throw only in DEBUG. (#085)
            if (_pendingTaskAndResolvers.TryRemove(errorMessage.TaskID, out SuccessAndFailActions? actions) == false)
            {
                _textOutput.Log($"Ignoring error reply for unknown or already-resolved task. Task ID: {errorMessage.TaskID} " +
                    $"Player ID: {errorMessage.PlayerID}");
#if DEBUG
                throw new ArgumentException($"Received error message trying to resolve task with ID that was not pending. Task ID: {errorMessage.TaskID} " +
                    $"Player ID: {errorMessage.PlayerID}");
#else
                return;
#endif
            }

            actions.OnFailed.Invoke(errorMessage.ErrorMessage);
        }

        private void DeserializeAndReturnReply<TReply>(string replyJson, TaskCompletionSource<TReply> replyTask)
        {
            TReply? reply = JsonConvert.DeserializeObject<TReply>(replyJson, _gameDataStore.GetJsonSettings());

            // A null reply is a legitimate "cancel / no selection" (e.g. a SelectionRequest's Back button),
            // NOT a deserialization failure — the requesting stage decides how to handle it. Treating null as
            // fatal here crashed the whole game on any networked cancel. Value-type replies that can't be null
            // already throw inside DeserializeObject, so this only forwards intentional reference-type nulls.
            replyTask.SetResult(reply!);
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
                : base($"Remote client returned an error after receiving request of type {requestType}: {errorMessage}. " +
                      $"Player ID: {playerID}.")
            { }
        }
    }
}