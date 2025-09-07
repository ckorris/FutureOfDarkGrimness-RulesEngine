using FDG.Network.Connection;
using System.Diagnostics;

namespace FDG.Network.Messages
{

    public interface IMessageRegistrar
    {
        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T> messageToUnsubscribe);
        public void DispatchToHandlers(object messageObject);

    }
    internal class MessageRegistrar : IMessageRegistrar
    {
        private readonly Dictionary<string, Type> _messageTypeRegistry = new Dictionary<string, Type>();

        private readonly Dictionary<Type, List<Delegate>> _messageHandlers = new Dictionary<Type, List<Delegate>>();

        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived)
        {
            Type typeKey = typeof(T);
            if (_messageHandlers.TryGetValue(typeKey, out List<Delegate>? handlers) == false)
            {
                handlers = new List<Delegate>();
                _messageHandlers[typeKey] = handlers;
            }

            handlers.Add(onMessageReceived);
        }

        public void DeregisterForMessageEvent<T>(Action<T> messageToUnsubscribe)
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

            Debug.WriteLine($"~Dispatching {messageObject.GetType()} message.");
            
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
