
using System;
using FDG.Data.Commands;

namespace FDG.Network
{
    public interface INetworkCommandClient
    {
        event Action<IGameCommand> OnCommandReceived;

        void SendCommand(IGameCommand command);
    }
}
