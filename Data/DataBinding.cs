
using FDG.Data.Commands;
using System;
using Vortice.Vulkan;

namespace FDG.Data
{
    public interface IDataBindingBase
    {
        DataReference Reference { get; }
    }
    public class DataBinding<T> : IDataBindingBase, IDisposable
    {
        public event Action<T, T>? OnValueChanged;

        public DataReference Reference { get; private set; }

        private CommandProcessor _commmandProcessor;
        private IReadableGameDataStore _readableGameDataStore;

        public DataBinding(CommandProcessor commandProcessor, IReadableGameDataStore readableGameDataStore, DataReference reference)
        {
            Reference = reference;
            _commmandProcessor = commandProcessor;
            _readableGameDataStore = readableGameDataStore;

            _commmandProcessor.RegisterBinding(reference, this);
        }

        public void SetValue(T value)
        {
            var setValueCommand = new SetValueCommand<T>(Reference, value);
            _commmandProcessor.ExecuteCommand(setValueCommand);

            //Don't notify directly here, because leaving it to the command processor allows it to happen
            //when the value is changed via the network.
        }

        public T GetValue( )
        {
            return _readableGameDataStore.GetValue<T>(Reference);
        }

        public void Dispose()
        {
            _commmandProcessor.DeregisterBinding(this);
        }

        internal void NotifyValueChanged(T oldValue, T newValue)
        {
            OnValueChanged?.Invoke(oldValue, newValue);
        }
    }
}
