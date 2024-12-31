using System.Collections.Generic;
using FDG.Data.Commands;
using FDG.Network;

namespace FDG.Data
{
    public interface ICommandProcessor
    {
        void RegisterBinding(DataReference reference, IDataBindingBase binding);

        void DeregisterBinding(IDataBindingBase binding);

        void ExecuteCommand(ICommand command);

        void RegisterNetworkClient(INetworkCommandClient networkClient);

        void DeregisterNetworkClient(INetworkCommandClient networkClient);
    }

    public class CommandProcessor : ICommandProcessor
    {
        private IReadWriteableGameDataStore _store;

        private Dictionary<DataReference, List<IDataBindingBase>> _bindings;

        private List<INetworkCommandClient> _networkClients;

        public CommandProcessor(IReadWriteableGameDataStore store)
        {
            _store = store;
            _bindings = new Dictionary<DataReference, List<IDataBindingBase>>();
            _networkClients = new List<INetworkCommandClient>();
        }

        public void RegisterBinding(DataReference reference, IDataBindingBase binding)
        {
            if (_bindings.ContainsKey(reference) == false)
            {
                _bindings[reference] = new List<IDataBindingBase>();
            }

            _bindings[reference].Add(binding);
        }

        public void DeregisterBinding(IDataBindingBase binding)
        {
            if(_bindings.ContainsKey(binding.Reference) == false)
            {
                return;
            }

            List<IDataBindingBase> dataBindingBases = _bindings[binding.Reference];
            dataBindingBases.Remove(binding);

            if(dataBindingBases.Count == 0)
            {
                _bindings.Remove(binding.Reference);
            }
        }

        public void ExecuteCommand(ICommand command)
        {
            foreach(INetworkCommandClient commandClient in _networkClients)
            {
                commandClient.SendCommand(command);
            }

            command.Execute(this);
        }

        public void RegisterNetworkClient(INetworkCommandClient networkClient)
        {
            _networkClients.Add(networkClient);
        }

        public void DeregisterNetworkClient(INetworkCommandClient networkClient)
        {
            _networkClients.Remove(networkClient);
        }

        //TODO: It feels weird to put this here, but I suppose we'll have a limited number of calls like this.
        internal void SetValue<T>(DataReference reference, T value)
        {
            T oldValue = _store.GetValue<T> (reference);

            _store.SetValue(reference, value);

            //Notify any and all bindings.
            if(_bindings.TryGetValue(reference, out List<IDataBindingBase> bindings))
            {
                foreach(DataBinding<T> binding in bindings)
                {
                    binding.NotifyValueChanged(oldValue, value);
                }
            }
        }
    }
}
