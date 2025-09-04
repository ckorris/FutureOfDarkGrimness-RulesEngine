using FDG.Network.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{

    public interface IMessageRegistrar
    {
        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe);
        public void DispatchToHandlers(object messageObject);

    }
    internal class MessageRegistrar : IMessageRegistrar
    {
        //private readonly Dictionary<string, Type> _messageTypeRegistry = new Dictionary<string, Type>();

        private readonly Dictionary<Type, List<Delegate>> _messageHandlers = new Dictionary<Type, List<Delegate>>();

        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived)
        {
            Type typeKey = typeof(T);
            if (_messageHandlers.TryGetValue(typeKey, out List<Delegate>? handlers) == false)
            {
                handlers = new List<Delegate>();
                _messageHandlers[typeKey] = handlers;
            }

            handlers.Add(onMessageReceived);
        }

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe)
        {
            Type typeKey = typeof(T);
            if (_messageHandlers.TryGetValue(typeKey, out List<Delegate>? handlers))
            {
                handlers.Remove(messageToUnsubscribe);
            }
        }

        public void DispatchToHandlers(object messageObject)
        {
            Type actualType = messageObject.GetType();
            if (_messageHandlers.TryGetValue(actualType, out List<Delegate>? handlers))
            {
                Debug.WriteLine($"Handlers: {handlers.Count}");

                foreach (Delegate del in handlers)
                {
                    del.DynamicInvoke(messageObject);
                }
            }
        }
    }
}
