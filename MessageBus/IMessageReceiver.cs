using FDG.Network.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.MessageBus
{
    internal interface IMessageReceiver
    {
        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe);
    }
}
