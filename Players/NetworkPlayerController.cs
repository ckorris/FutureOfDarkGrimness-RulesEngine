using FDG.Data;
using FDG.Network.Connection;
using FDG.StageResolution;

namespace FDG.Players
{
    public class NetworkPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = true; //May need to change.

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
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
        {
            return _requestMessageSender.ResolveRequestOverNetwork<TRequest, TReply>(request);
        }
    }
}
