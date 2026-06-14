using FDG.Network.Connection;
using System.Reflection;

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

        private readonly ITextOutput _textOutput;

        public MessageRegistrar(ITextOutput? textOutput = null)
        {
            _textOutput = textOutput ?? new ConsoleTextOutput();
        }

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
            
            if (_messageHandlers.TryGetValue(actualType, out List<Delegate>? handlers))
            {
                //Snapshot so a handler that (de)registers during dispatch doesn't mutate the list we're iterating.
                foreach (Delegate del in handlers.ToArray())
                {
                    try
                    {
                        del.DynamicInvoke(messageObject);
                    }
                    catch (Exception exception)
                    {
                        //Isolate handlers: one throwing handler must not abort the others or bubble into the
                        //read loop's catch-all (which would disconnect the client). Log and carry on.
                        Exception actual = (exception as TargetInvocationException)?.InnerException ?? exception;
                        _textOutput.Log($"Message handler for {actualType.Name} threw {actual.GetType().Name}: {actual.Message}");
                    }
                }
            }
        }
    }
}
