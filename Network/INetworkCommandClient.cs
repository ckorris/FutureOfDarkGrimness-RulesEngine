
using System;
using FDG.Data.Commands;

namespace FDG.Network
{
    public interface INetworkCommandClient
    {
        event Action<ICommand> OnCommandReceived;

        void SendCommand(ICommand command);
    }
}
