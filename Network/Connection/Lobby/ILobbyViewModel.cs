using FDG.Network.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Connection.Lobby
{
    public interface ILobbyViewModel : IDisposable
    {
        IObservable<string> ServerName { get; }

        IObservable<LobbyChatMessage> ChatMessages { get; }

        void SendMessage(string message);
    }
}
