
using Newtonsoft.Json;

namespace FDG.Data
{
    public interface IComponentStore
    {
        bool IsValid(DataReference reference, out EInvalidReason reason);

        void SetValue(DataReference reference, object newValue);

        bool Destroy(DataReference reference);
    }

    public class ComponentStore<T> : IComponentStore
    {
        public event Action<DataReference, T>? OnComponentAdded;
        public event Action<DataReference, T> OnAnyUpdatedTyped;
        public event Action<DataReference, T>? OnComponentRemoved;

        //Action when any value changed.

        private T[] _data;
        private bool[] _used;
        private int[] _generations;
        private int _capacity;

        private TypeID _typeID;

        private Dictionary<int, DataBinding<T>> _bindings;

        public ComponentStore(int capacity, TypeID typeID)
        {
            _capacity = capacity;
            _data = new T[capacity];
            _used = new bool[capacity];
            _generations = new int[capacity];
            _typeID = typeID;
            _bindings = new Dictionary<int, DataBinding<T>>();
        }

        public DataReference Create(T initialValue)
        {
            for (int i = 0; i < _capacity; i++)
            {
                if( _used[i] == false)
                {
                    _used[i] = true;
                    _generations[i]++;
                    _data[i] = initialValue;

                    DataReference dataReference = new DataReference()
                    {
                        TypeID = _typeID,
                        Index = i,
                        Generation = _generations[i]
                    };

                    OnComponentAdded?.Invoke(dataReference, initialValue);

                    return dataReference;
                }
            }

            throw new ExceededDataTypeCapacityException(_capacity);
        }

        public bool Destroy(DataReference reference)
        {
            if(IsValid(reference, out _) == false)
            {
                return false;
            }

            _data[reference.Index] = default; //Technically unnecessary, but keeps things clean.
            _used[reference.Index] = false;

            OnComponentRemoved?.Invoke(reference, _data[reference.Index]);

            if (_bindings.ContainsKey(reference.Index))
            {
                _bindings[reference.Index].Invalidate();
                _bindings.Remove(reference.Index);
            }

            return true;
        }

        public T GetValue(DataReference reference)
        {
            if(IsValid(reference, out EInvalidReason failReason) == false)
            {
                throw new InvalidDataReferenceException(reference, failReason);
            }

            return _data[reference.Index];
        }

        public void SetValue(DataReference reference, T value)
        {
            if (value == null)
            {
                throw new NullReferenceException();
            }

            if (IsValid(reference, out EInvalidReason failReason) == false)
            {
                throw new InvalidDataReferenceException(reference, failReason);
            }

            T oldValue = _data[reference.Index];
            _data[reference.Index] = value;

            if(_bindings.ContainsKey(reference.Index))
            {
                _bindings[reference.Index].NotifyValueChanged(oldValue, value);
            }

            OnAnyUpdatedTyped?.Invoke(reference, value);
        }

        public void SetValue(DataReference reference, object newValue)
        {
            if (newValue is T typedNewValue)
            {
                SetValue(reference, typedNewValue);
            }
            else
            {
                throw new InvalidCastException($"Passed in object that was not type {typeof(T)}.");
            }
        }

        public IEnumerable<T> GetAllValues()
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (_used[i])
                {
                    yield return _data[i];
                }
            }
        }

        public IEnumerable<DataReference> GetAllDataReferences()
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (_used[i])
                {
                    yield return new DataReference()
                    {
                        TypeID = _typeID,
                        Index = i,
                        Generation = _generations[i]
                    };
                }
            }
        }

        public bool IsValid(DataReference reference, out EInvalidReason reason)
        {
            if (reference.TypeID.ID != _typeID.ID)
            {
                reason = EInvalidReason.IncorrectType;
                return false;
            }
            if (reference.Index < 0 || reference.Index >= _capacity)
            {
                reason = EInvalidReason.IndexExceedsCapacity;
                return false;
            }
            if (_used[reference.Index] == false)
            {
                reason = EInvalidReason.IsNotAssigned;
                return false;
            }
            if (_generations[reference.Index] != reference.Generation)
            {
                reason = _generations[reference.Index] < reference.Index
                    ? EInvalidReason.OutdatedGeneration
                    : EInvalidReason.FutureGeneration;
                return false;
            }

            reason = EInvalidReason.Valid;
            return true;
        }

        public DataBinding<T> GetDataBinding(DataReference dataReference)
        {
            if(IsValid(dataReference, out EInvalidReason reason) == false)
            {
                throw new InvalidDataReferenceException(dataReference, reason);
            }

            if(_bindings.ContainsKey(dataReference.Index) == false)
            {
                DataBinding<T> dataBinding = new DataBinding<T>(dataReference, this);
                _bindings.Add(dataReference.Index, dataBinding);
                return dataBinding;
            }

            return _bindings[dataReference.Index];
        }

        private class ExceededDataTypeCapacityException : Exception
        {
            public ExceededDataTypeCapacityException(int maxCapacity )
                : base($"Exceeded max capacity of entries of type {typeof(T)}, which was set to {maxCapacity}.") { }
        }

        private class InvalidDataReferenceException : Exception
        {
            public InvalidDataReferenceException(DataReference reference, EInvalidReason reason)
                : base($"Passed reference for type {typeof(T)} was invalid. Reason: {reason}. Reference: {reason}") { }
        }
    }

    public enum EInvalidReason
    {
        Valid,
        IsNotAssigned,
        IncorrectType,
        OutdatedGeneration,
        FutureGeneration,
        IndexExceedsCapacity,
        TypeNotRegistered
    }
}

