using FDG.GameModel;
using FDG.StageResolution;
using FDG.TempVisuals;

namespace FDG.Players
{
    public class ComputerPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady => true;

        public ITempVisualDrawer? TempVisualDrawer => null;

        public event Action<bool>? OnReadyStateChanged;
        public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;

        public ComputerPlayerController(string name, PlayerID id, FDGGame_AsLocal localGame,
            IStageResolverRegistry registry)
        {
            Name = name;
            ID = id;
            localGame.AddLocalPlayerID(id);
            localGame.AssignInterfaces(null, null, registry, null, null);
        }

        public Task WaitUntilReadyAsync() => Task.CompletedTask;

        public void SendLogMessage(string logMessage) { }

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
    }
}
