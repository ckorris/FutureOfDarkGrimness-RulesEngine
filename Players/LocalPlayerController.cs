using FDG.GameModel;
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

        public event Action<bool>? OnReadyStateChanged;
        //public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        private FDGGame_AsLocal _localPlayer;

        public LocalPlayerController(string name, PlayerID id, FDGGame_AsLocal localPlayer)
        {
            Name = name;
            ID = id;
            _localPlayer = localPlayer;
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
    }
}
