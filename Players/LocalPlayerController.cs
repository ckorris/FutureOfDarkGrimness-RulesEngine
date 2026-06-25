using FDG.GameModel;
using FDG.Presentation;
using FDG.TextInterface;
using System.Diagnostics;

namespace FDG.Players
{
    public class LocalPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = true;

        public IPresentationSink? PresentationSink => _localPlayer.PresentationSink;

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        private FDGGame_AsLocal _localPlayer;

        public LocalPlayerController(string name, PlayerID id, FDGGame_AsLocal localPlayer)
        {
            Name = name;
            ID = id;
            _localPlayer = localPlayer;

            // Subscribe to the player messages once they're assigned. EnsureMessageSubscription is idempotent
            // (it removes before adding) and runs both now and on every resolver assignment, so there's no
            // window where the UI is null when we check yet non-null when the deferred handler runs, and no
            // chance of double-subscribing if assignment fires more than once.
            EnsureMessageSubscription();
            _localPlayer.OnStageResolverAssigned += EnsureMessageSubscription;
        }

        private void EnsureMessageSubscription()
        {
            IPlayerMessageUI playerMessageUI = _localPlayer.PlayerMessageUI;
            if (playerMessageUI == null)
            {
                return;
            }

            playerMessageUI.OnMessageSentByPlayer -= OnPlayerSentMessage;
            playerMessageUI.OnMessageSentByPlayer += OnPlayerSentMessage;
        }

        private void OnPlayerSentMessage(string message, EChatMessageType messageType)
        {
            OnMessageSentByPlayer?.Invoke(ID, messageType, message);
        }

        public Task WaitUntilReadyAsync()
        {
            if(_localPlayer.StageResolverRegistry != null)
            {
                Debug.WriteLine("Local player was ready when queried.");

                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> source
                = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);


            void Handler()
            {
                _localPlayer.OnStageResolverAssigned -= Handler;
                Debug.WriteLine("Local player became ready.");

                source.SetResult(true);
            }

            _localPlayer.OnStageResolverAssigned += Handler;

            return source.Task;
        }

        public void SendLogMessage(string logMessage, TextColor color)
        {
            _localPlayer.LogMessageUI?.DisplayLogMessage(logMessage, color);
        }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
        {
            _localPlayer.PlayerMessageUI?.DisplayPlayerMessage(sendingPlayerName, messageType, message);
        }
    }
}
