using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.StageResolution;
using FDG.TempVisuals;

namespace FDG.Players
{
    public class NetworkPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = false; //May need to change.

        public ITempVisualDrawer? TempVisualDrawer { get; }

        private ConnectionID _connectionID;

        private IMessageBusHost _messageBusHost; 

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        public NetworkPlayerController(string name, PlayerID playerID, ConnectionID connectionID, 
            IMessageBusHost messageBusHost, IReadableGameDataStore gameDataStore)
        {
            Name = name;
            ID = playerID;
            _connectionID = connectionID;
            _messageBusHost = messageBusHost;

            _messageBusHost.RegisterForMessageEvent<PostLaunchPlayerReadyMessage>(OnPlayerReadyMessageReceived);

            _messageBusHost.RegisterForMessageEvent<NetworkPlayerSubmitChatMessage>(OnPlayerChatMessageReceived);

            //TODO: THis needs to be moved elsewhere.
            TempVisualDrawer = new NetworkedTempVisualDrawer(_messageBusHost, connectionID);
        }

        private void OnPlayerChatMessageReceived(NetworkPlayerSubmitChatMessage message)
        {
            /*
            if(iD != _connectionID)
            {
                return; //Not this player.
            }
            */

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
            if (message.ReadyPlayerID == ID)
            {
                IsReady = true;
                OnReadyStateChanged?.Invoke(true);
            }
        }

        public void SendLogMessage(string logMessage)
        {
            LogChatNetworkMessage messageRecord = new LogChatNetworkMessage(logMessage);
            _messageBusHost.SendCommandToAllAsync(messageRecord);
        }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
        {
            PlayerChatNetworkMessage messageRecord = new PlayerChatNetworkMessage(sendingPlayerName, messageType, message);
            _messageBusHost.SendCommandToAllAsync(messageRecord);
        }
    }
}
