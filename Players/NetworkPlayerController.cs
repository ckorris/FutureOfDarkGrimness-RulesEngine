using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.StageResolution;

namespace FDG.Players
{
    public class NetworkPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        // The connection this networked player is on. Exposed so the disconnect lifecycle (#076) can map a
        // dropped ConnectionID back to its PlayerID and fail that player's pending decision requests.
        public ConnectionID ConnectionID { get; }

        public bool IsReady { get; private set; } = false; //May need to change.

        public IPresentationSink? PresentationSink { get; }

        private IMessageBusHost _messageBusHost;

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        public NetworkPlayerController(string name, PlayerID playerID, ConnectionID connectionID,
            IMessageBusHost messageBusHost, IReadableGameDataStore gameDataStore)
        {
            Name = name;
            ID = playerID;
            ConnectionID = connectionID;
            _messageBusHost = messageBusHost;

            _messageBusHost.RegisterForMessageEvent<PostLaunchPlayerReadyMessage>(OnPlayerReadyMessageReceived);
            _messageBusHost.RegisterForMessageEvent<NetworkPlayerSubmitChatMessage>(OnPlayerChatMessageReceived);

            PresentationSink = new NetworkedPresentationSink(_messageBusHost, connectionID);
        }

        private void OnPlayerChatMessageReceived(NetworkPlayerSubmitChatMessage message)
        {
            // Every network player's controller is registered for this message, so filter to the one
            // representing the sender. Without this each controller would re-raise OnMessageSentByPlayer
            // (and the relayer re-broadcast the chat) once per network player, misattributed (#077).
            if (message.PlayerID != ID)
            {
                return;
            }

            OnMessageSentByPlayer?.Invoke(ID, message.MessageType, message.Message);
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

        private void OnPlayerReadyMessageReceived(PostLaunchPlayerReadyMessage message)
        {
            // Guard against duplicate ready messages: once ready, a repeat must not re-fire OnReadyStateChanged
            // (the bus can deliver the same PostLaunchPlayerReadyMessage more than once).
            if (message.ReadyPlayerID == ID && IsReady == false)
            {
                IsReady = true;
                OnReadyStateChanged?.Invoke(true);
            }
        }

        public void SendLogMessage(string logMessage, TextColor color)
        {
            LogChatNetworkMessage messageRecord = new LogChatNetworkMessage(logMessage, color);
            _messageBusHost.SendCommandToAllAsync(messageRecord);
        }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
        {
            PlayerChatNetworkMessage messageRecord = new PlayerChatNetworkMessage(sendingPlayerName, messageType, message);
            _messageBusHost.SendCommandToAllAsync(messageRecord);
        }
    }
}
