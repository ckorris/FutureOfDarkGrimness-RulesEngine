using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.StageResolution;

namespace FDG.Players
{
    public class NetworkPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = false; //May need to change.

        private ConnectionID _connectionID;

        private ICommandDispatcher _commandDispatcher; 

        private NetworkRequestMessageSender _requestMessageSender;

        public event Action<bool>? OnReadyStateChanged;

        public NetworkPlayerController(string name, PlayerID playerID, ConnectionID connectionID, ICommandDispatcher commandDispatcher,
            IReadableGameDataStore gameDataStore)
        {
            Name = name;
            ID = playerID;
            _connectionID = connectionID;
            _commandDispatcher = commandDispatcher;
            _requestMessageSender = new NetworkRequestMessageSender(playerID, connectionID, commandDispatcher, gameDataStore);

            _commandDispatcher.RegisterForMessageEvent<PostLaunchPlayerReadyMessage>(OnPlayerReadyMessageReceived);
        }

        public Task WaitUntilReadyAsync()
        {
            if (IsReady)
            {
                System.Diagnostics.Debug.WriteLine("Networked player was ready when queried.");

                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> source
                = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(bool ready)
            {
                if (ready == false)
                {
                    return;
                }

                OnReadyStateChanged -= Handler;
                System.Diagnostics.Debug.WriteLine("Networked player became ready.");

                source.SetResult(true);
            }

            OnReadyStateChanged += Handler;
            return source.Task;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
        {
            return _requestMessageSender.ResolveRequestOverNetwork<TRequest, TReply>(request);
        }

        private void OnPlayerReadyMessageReceived(PostLaunchPlayerReadyMessage message, ConnectionID _)
        {
            if (message.ReadyPlayerID == ID)
            {
                IsReady = true;
                OnReadyStateChanged?.Invoke(true);
            }
        }

        public void SendLogMessage(string logMessage)
        {
            LogChatNetworkMessage messageRecord = new LogChatNetworkMessage(logMessage);
            _commandDispatcher.SendCommandAsync(messageRecord);
        }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
        {
            PlayerChatNetworkMessage messageRecord = new PlayerChatNetworkMessage(sendingPlayerName, messageType, message);
            _commandDispatcher.SendCommandAsync(messageRecord);
        }
    }
}
