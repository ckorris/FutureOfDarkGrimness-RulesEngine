using FDG.MessageBus;
using FDG.Network.Connection;

namespace FDG.Tests
{
    // In-process IMessageBusHost/IMessageBusClient for the resume test — the engine-side twin of
    // the app's LocalMessageBus (single dictionary of handlers, synchronous dispatch, host id for
    // connection-aware handlers).
    internal sealed class InProcessBus : IMessageBusHost, IMessageBusClient
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public event Action<ConnectionID>? OnClientDisconnected;

        // There are no real connections in-process, so a test drives this by hand (#187): it is the
        // signal RequestMessageSender listens on to fail a dropped player's decision requests.
        internal void SimulateClientDisconnected(ConnectionID connectionID) =>
            OnClientDisconnected?.Invoke(connectionID);

        public void RegisterForMessageEvent<T>(Action<T> handler) => Add(typeof(T), handler);
        public void DeregisterForMessageEvent<T>(Action<T> handler) => Remove(typeof(T), handler);
        public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Add(typeof(T), handler);
        public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Remove(typeof(T), handler);

        public Task SendCommandToAllAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
        public Task SendCommandToHostAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID) { Dispatch(message); return Task.CompletedTask; }
        public Task SendCommandToLocalAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
        public void Dispose() { }

        private void Add(Type type, Delegate handler)
        {
            if (!_handlers.TryGetValue(type, out var list)) _handlers[type] = list = new List<Delegate>();
            list.Add(handler);
        }

        private void Remove(Type type, Delegate handler)
        {
            if (_handlers.TryGetValue(type, out var list)) list.Remove(handler);
        }

        private void Dispatch<TMessage>(TMessage message)
        {
            if (message == null || !_handlers.TryGetValue(typeof(TMessage), out var list)) return;
            foreach (Delegate handler in list.ToList())
            {
                if (handler is Action<TMessage> plain) plain(message);
                else if (handler is Action<TMessage, ConnectionID> withConnection) withConnection(message, ConnectionID.Host);
            }
        }
    }
}
