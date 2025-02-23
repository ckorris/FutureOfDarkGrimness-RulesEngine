using FDG.Network.Connection;
using FDG.StageResolution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    public class NetworkPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        private ConnectionID _connectionID;

        private ICommandDispatcher _commandDispatcher;

        public NetworkPlayerController(string name, PlayerID playerID, ConnectionID connectionID, ICommandDispatcher commandDispatcher)
        {
            Name = name;
            ID = playerID;
            _connectionID = connectionID;
            _commandDispatcher = commandDispatcher;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
        {
            throw new NotImplementedException();
        }
    }
}
