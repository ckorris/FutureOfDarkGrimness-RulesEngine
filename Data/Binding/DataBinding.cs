

namespace FDG.Data
{
    public interface IDataBindingBase
    {
        DataReference Reference { get; }
    }

    public class DataBinding<T> : IDataBindingBase
    {
        public event DataValueChangedHandler<T>? OnValueChanged;

        public DataReference Reference { get; private set; }

        public bool IsValid { get; private set; }

        private readonly ComponentStore<T> _componentStore;

        public DataBinding(DataReference reference, ComponentStore<T> componentStore)
        {
            Reference = reference;
            _componentStore = componentStore;
            IsValid = true;
        }

        public void SetValue(T value)
        {
            _componentStore.SetValue(Reference, value);

            //Don't notify directly here, because leaving it to the command processor allows it to happen
            //when the value is changed via the network.
        }

        public T GetValue( )
        {
            return _componentStore.GetValue(Reference);
        }

        internal void NotifyValueChanged(T oldValue, T newValue)
        {
            OnValueChanged?.Invoke(oldValue, newValue);
        }

        internal void Invalidate()
        {
            IsValid = false;
        }

        public static implicit operator T(DataBinding<T> dataBinding)
        {
            return dataBinding.GetValue();
        }
    }
}
