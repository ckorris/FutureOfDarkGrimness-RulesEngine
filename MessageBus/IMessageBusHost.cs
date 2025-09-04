using FDG.Network.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.MessageBus
{
    public interface IMessageBusHost : IMessageReceiver
    {
        public Task SendCommandToAllAsync<TMessage>(TMessage message);

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID);

    }
}
