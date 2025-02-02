using FDG.Network.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    public interface IPlayer : IPlayerIdentifyable
    {
        public EPlayerType PlayerType { get; }

        public ICommandDispatcher CommandDispatcher { get; }
    }


}
