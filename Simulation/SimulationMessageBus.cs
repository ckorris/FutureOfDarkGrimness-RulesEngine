using FDG.MessageBus;
using FDG.Network.Connection;

namespace FDG.Simulation
{
    /// <summary>
    /// In-process bus for a simulated game - one per simulation, never shared. The engine-side twin
    /// of the app's <c>LocalMessageBus</c>, FdgLab's <c>LabMessageBus</c> and the tests'
    /// <c>InProcessBus</c>.
    /// <para>
    /// A simulation's DECISIONS do not travel this bus (see <see cref="DirectPlayerRequester"/>),
    /// but <see cref="GameModel.FDGServer"/> still constructs a data synchronizer, a token-change
    /// broadcaster and a preview relayer against one, so a simulation needs a bus that accepts
    /// messages and drops them. Nothing subscribes, so dispatch is a dictionary miss.
    /// </para>
    /// </summary>
    public sealed class SimulationMessageBus : IMessageBusHost, IMessageBusClient
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

#pragma warning disable CS0067 // A simulation has no connections to drop.
        public event Action<ConnectionID>? OnClientDisconnected;
#pragma warning restore CS0067

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
            if (!_handlers.TryGetValue(type, out List<Delegate>? list)) _handlers[type] = list = new List<Delegate>();
            list.Add(handler);
        }

        private void Remove(Type type, Delegate handler)
        {
            if (_handlers.TryGetValue(type, out List<Delegate>? list)) list.Remove(handler);
        }

        private void Dispatch<TMessage>(TMessage message)
        {
            if (message == null || !_handlers.TryGetValue(typeof(TMessage), out List<Delegate>? list)) return;
            foreach (Delegate handler in list.ToList())
            {
                if (handler is Action<TMessage> plain) plain(message);
                else if (handler is Action<TMessage, ConnectionID> withConnection) withConnection(message, ConnectionID.Host);
            }
        }
    }
}
