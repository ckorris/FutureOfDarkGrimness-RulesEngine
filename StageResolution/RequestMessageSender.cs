using FDG.Data;
using FDG.MessageBus;
using FDG.Network;
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

        // Players whose connection has dropped. A request targeting one of these is faulted the moment it's
        // made — not just the ones in flight at disconnect time — so a drop that lands while it isn't that
        // player's turn still ends the game at their next request instead of hanging on a dead connection.
        // Written on the network read thread (disconnect), read on the engine thread (RequestDecision).
        private readonly ConcurrentDictionary<PlayerID, byte> _disconnectedPlayers
            = new ConcurrentDictionary<PlayerID, byte>();

        public RequestMessageSender(IMessageBusHost messageBusHost, IReadableGameDataStore gameDataStore,
            PlayerSlotManager playerSlotManager, ITextOutput textOutput)
        {
            _messageBusHost = messageBusHost;
            _gameDataStore = gameDataStore;
            _playerSlotManager = playerSlotManager;
            _textOutput = textOutput;

            _messageBusHost.RegisterForMessageEvent<StageTaskReplyMessage>(OnReceivedReplyMessage);
            _messageBusHost.RegisterForMessageEvent<StageTaskRequestErrorMessage>(OnReceivedErrorMessage);
            _messageBusHost.OnClientDisconnected += OnClientDisconnected;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) 
            where TRequest : IStageTaskRequest<TReply>
        {
            PlayerID targetPlayerID = request.TargetPlayerID;

            // A player who has already dropped will never answer. Fault the request on arrival rather than
            // sending it into a dead connection and hanging — this is what catches a disconnect that landed
            // while it wasn't this player's turn (the in-flight fail path alone can't, there was nothing
            // pending then). The awaiting stage rethrows PlayerDisconnectedException, which FDGServer turns
            // into a graceful game-end.
            if (_disconnectedPlayers.ContainsKey(targetPlayerID))
            {
                TaskCompletionSource<TReply> alreadyGone =
                    new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                alreadyGone.SetException(new PlayerDisconnectedException(targetPlayerID, "Player disconnected."));
                return alreadyGone.Task;
            }

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

            // Serialize the outbound request with the wire settings so the $type BindToName written
            // here matches what the receiving client's wire binder will accept (#186) — same reason
            // the reply body uses them. BindToName is identical to the store binder, so the produced
            // bytes are unchanged; this is drift-proofing, not a format change.
            string? requestJson = JsonConvert.SerializeObject(request, WireJsonSettings.For(_gameDataStore));

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
            // Used by the disconnect path to fault the awaiting task directly with a typed exception.
            // TrySet* because a reply could race the disconnect; the TryRemove claim already makes one win.
            Action<Exception> failWithException = (exception) => taskCompletionSource.TrySetException(exception);

            SuccessAndFailActions actions = new SuccessAndFailActions(targetPlayerID, onSuccess, onFailed, failWithException);

            _pendingTaskAndResolvers.TryAdd(taskID, actions);

            // Send the awaiting notification BEFORE the request message so OutstandingTaskLister
            // entries are populated before any synchronous resolver (e.g. AI) can produce a reply
            // and trigger the resolve notification — otherwise the resolve fires against an empty
            // lister dict and the entry that arrives next stays stuck forever.
            // This notification stays broadcast: every client shows "waiting on Player N" in its
            // outstanding-task UI. Snapshot the slot info by value (the message no longer carries a
            // live binding — see StageTaskNotifyAwaitingMessage, #088).
            PlayerSlotInfo targetSlotInfo = _playerSlotManager.GetSlotByID(request.TargetPlayerID).InfoBinding.GetValue();
            StageTaskNotifyAwaitingMessage awaitingMessage = new StageTaskNotifyAwaitingMessage(taskID, targetSlotInfo, request.TaskName);
            _messageBusHost.SendCommandToAllAsync(awaitingMessage);

            // Route the decision payload to just the target player instead of broadcasting it to everyone
            // (#088): a remote player receives it on their own connection; a local (in-process) player gets
            // an in-process dispatch. The receiver-side PlayerID filter still guards the local case (local
            // players share one registrar), but no client ever receives another player's request payload —
            // saving bandwidth and not baking in the open-information assumption.
            if (_playerSlotManager.TryGetConnectionByPlayerID(targetPlayerID, out ConnectionID targetConnection))
            {
                _messageBusHost.SendCommandToSingleAsync(requestMessage, targetConnection);
            }
            else
            {
                _messageBusHost.SendCommandToLocalAsync(requestMessage);
            }

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

        private void OnClientDisconnected(ConnectionID connectionID)
        {
            // Map the dropped connection back to the player on it. A connection with no game slot (a
            // spectator, or one already torn down) has nothing pending to fail.
            if (_playerSlotManager.TryGetPlayerIDByConnection(connectionID, out PlayerID playerID) == false)
            {
                return;
            }

            // Record the drop before failing in-flight requests, so a request racing in during the fail is
            // rejected by RequestDecision's guard rather than slipping through to the dead connection.
            _disconnectedPlayers.TryAdd(playerID, 0);

            FailPendingRequestsForPlayer(playerID, "Player disconnected.");
        }

        /// <summary>
        /// Fails every decision request still awaiting a reply from <paramref name="playerID"/> with a
        /// <see cref="PlayerDisconnectedException"/>, so the awaiting stage code stops hanging forever when a
        /// player drops (#076). Each task is atomically claimed via <c>TryRemove</c> (as the reply/error
        /// handlers do), so a reply that races the disconnect still resolves the task exactly once, and the
        /// task is cleared from every client's outstanding-task UI just like a normal resolve.
        /// </summary>
        public void FailPendingRequestsForPlayer(PlayerID playerID, string reason)
        {
            int failedCount = 0;

            foreach (KeyValuePair<TaskID, SuccessAndFailActions> pending in _pendingTaskAndResolvers.ToArray())
            {
                if (pending.Value.TargetPlayerID != playerID)
                {
                    continue;
                }

                if (_pendingTaskAndResolvers.TryRemove(pending.Key, out SuccessAndFailActions? actions) == false)
                {
                    continue; //A racing reply already claimed this task.
                }

                //Clear it from every client's outstanding-task UI, same as a normal resolve.
                _messageBusHost.SendCommandToAllAsync(new StageTaskNotifyResolvedMessage(pending.Key));

                actions!.FailWithException.Invoke(new PlayerDisconnectedException(playerID, reason));
                failedCount++;
            }

            if (failedCount > 0)
            {
                _textOutput.Log($"Player {playerID} disconnected - failed {failedCount} pending request(s): {reason}");
            }
        }

        private void DeserializeAndReturnReply<TReply>(string replyJson, TaskCompletionSource<TReply> replyTask)
        {
            // replyJson is attacker-controlled: a networked client computed and sent it. Deserialize
            // it through the wire allowlist (#186), NOT the store's permissive settings — a reply
            // body carries polymorphic $type (CancellableResult<T> etc.), which is exactly the RCE
            // surface the envelope hardening would otherwise leave open here.
            TReply? reply = JsonConvert.DeserializeObject<TReply>(replyJson, WireJsonSettings.For(_gameDataStore));

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
            _messageBusHost.OnClientDisconnected -= OnClientDisconnected;
        }



        private class SuccessAndFailActions
        {
            public readonly PlayerID TargetPlayerID; //Whose reply this task awaits — used to fail on disconnect.
            public readonly Action<string> OnSuccessful; //Call with Json
            public readonly Action<string> OnFailed; //Call with error message.
            public readonly Action<Exception> FailWithException; //Fault the awaiting task directly.

            public SuccessAndFailActions(PlayerID targetPlayerID, Action<string> onSuccessful,
                Action<string> onFailed, Action<Exception> failWithException)
            {
                TargetPlayerID = targetPlayerID;
                OnSuccessful = onSuccessful;
                OnFailed = onFailed;
                FailWithException = failWithException;
            }
        }

        public class NetworkedRequestFailedException : Exception
        {
            public NetworkedRequestFailedException(PlayerID playerID, string requestType, string errorMessage)
                : base($"Remote client returned an error after receiving request of type {requestType}: {errorMessage}. " +
                      $"Player ID: {playerID}.")
            { }
        }

        // Thrown into a stage awaiting a decision from a player who disconnected, so the awaiting code stops
        // hanging and the fault carries the departed player's identity (#076).
        public class PlayerDisconnectedException : Exception
        {
            public PlayerID PlayerID { get; }

            public PlayerDisconnectedException(PlayerID playerID, string reason)
                : base($"Player {playerID} disconnected before answering a pending request: {reason}")
            {
                PlayerID = playerID;
            }
        }
    }
}