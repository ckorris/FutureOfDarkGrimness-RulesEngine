using System.Collections.Generic;
using FDG.Data.Commands;

namespace FDG.Data
{
    public class CommandProcessor
    {
        private IReadWriteableGameDataStore _store;

        private Dictionary<DataReference, List<IDataBindingBase>> _bindings;

        public CommandProcessor(IReadWriteableGameDataStore store)
        {
            _store = store;
            _bindings = new Dictionary<DataReference, List<IDataBindingBase>>();
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
            //TODO: Networking.
            command.Execute(this);
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
