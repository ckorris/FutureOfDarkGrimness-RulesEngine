using FDG.GameModel;
using FDG.Presentation;
using FDG.TempVisuals;
using System.Diagnostics;

namespace FDG.Players
{
    public class LocalPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = true;

        public ITempVisualDrawer? TempVisualDrawer => _localPlayer.TempVisualDrawer;

        public IPresentationSink? PresentationSink => _localPlayer.PresentationSink;

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        private FDGGame_AsLocal _localPlayer;

        public LocalPlayerController(string name, PlayerID id, FDGGame_AsLocal localPlayer)
        {
            Name = name;
            ID = id;
            _localPlayer = localPlayer;

            //Subscribe to the player messages once they're assigned.
            if(_localPlayer.PlayerMessageUI != null)
            {
                localPlayer.PlayerMessageUI.OnMessageSentByPlayer += OnPlayerSentMessage;
            }
            else
            {
                _localPlayer.OnStageResolverAssigned += () =>
                    localPlayer.PlayerMessageUI.OnMessageSentByPlayer += OnPlayerSentMessage;
            }
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

        public void SendLogMessage(string logMessage)
        {
            _localPlayer.LogMessageUI?.DisplayLogMessage(logMessage);
        }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
        {
            _localPlayer.PlayerMessageUI?.DisplayPlayerMessage(sendingPlayerName, messageType, message);
        }
    }
}
