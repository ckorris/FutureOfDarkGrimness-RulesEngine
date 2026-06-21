
using FDG.Data.Containers;

namespace FDG.Data
{
    public interface IComponentStore
    {
        int Capacity { get; }

        bool IsValid(DataReference reference, out EInvalidReason reason);

        void CreateFromReference(DataReference existingReference, object initialValue);

        void SetValue(DataReference reference, object newValue);

        object? GetValueUntyped(DataReference reference);

        IEnumerable<DataReference> GetAllDataReferences();

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

        public int Capacity => _capacity;

        public ComponentStore(int capacity, TypeID typeID)
        {
            _capacity = capacity;
            _data = new T[capacity];
            _used = new bool[capacity];
            _generations = new int[capacity];
            _typeID = typeID;
            _bindings = new Dictionary<int, DataBinding<T>>();
        }

        // Upper bound on a single grow request. Local Create only ever needs +1, so this only bites
        // CreateFromReference when a foreign (save/network) reference carries a corrupt index — we'd
        // rather throw a typed exception than try to allocate a multi-gigabyte array.
        private const int MAX_CAPACITY = 1 << 24; // ~16.7M entries

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

            // No free slot — grow rather than throw. DataReferences are {TypeID, Index, Generation}
            // value identities, so reallocating the backing arrays keeps every existing reference
            // valid; the first newly-added slot (at the old capacity) is the one we hand out.
            int freeIndex = _capacity;
            EnsureCapacity(_capacity + 1);

            _used[freeIndex] = true;
            _generations[freeIndex]++;
            _data[freeIndex] = initialValue;

            DataReference grownReference = new DataReference()
            {
                TypeID = _typeID,
                Index = freeIndex,
                Generation = _generations[freeIndex]
            };

            OnComponentAdded?.Invoke(grownReference, initialValue);

            return grownReference;
        }

        // Grows the backing arrays so index <paramref name="requiredLength"/> - 1 is addressable.
        // Doubles to amortize repeated growth. No-op when already large enough.
        private void EnsureCapacity(int requiredLength)
        {
            if (requiredLength <= _capacity)
            {
                return;
            }

            if (requiredLength > MAX_CAPACITY)
            {
                throw new ExceededDataTypeCapacityException(requiredLength);
            }

            int newCapacity = _capacity <= 0 ? 4 : _capacity;
            while (newCapacity < requiredLength)
            {
                newCapacity *= 2;
            }
            newCapacity = Math.Min(newCapacity, MAX_CAPACITY);

            Array.Resize(ref _data, newCapacity);
            Array.Resize(ref _used, newCapacity);
            Array.Resize(ref _generations, newCapacity);
            _capacity = newCapacity;
        }

        public void CreateFromReference(DataReference existingReference, object initialValue)
        {
            //A negative index can never be valid. A high index is fine — this path replays foreign
            //references (save load / network sync) whose source store may have grown past ours, so
            //grow to fit rather than reject. EnsureCapacity throws if the index is implausibly large.
            if(existingReference.Index < 0)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.IndexExceedsCapacity);
            }

            EnsureCapacity(existingReference.Index + 1);

            if(existingReference.TypeID != _typeID)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.IncorrectType);
            }

            if (_used[existingReference.Index] == true)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.IndexAlreadyAssigned);
            }

            if (_generations[existingReference.Index] < existingReference.Generation - 1)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.FutureGeneration);
            }

            if(_generations[existingReference.Index] >= existingReference.Generation)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.OutdatedGeneration);
            }

            T typedInitValue = (T)initialValue;

            if(typedInitValue == null)
            {
                throw new ArgumentException($"Passed in an object to {typeof(ComponentStore<T>)} that could not be " + 
                    $"converted to a {typeof(T)}.");
            }

            _used[existingReference.Index] = true;
            _generations[existingReference.Index] = existingReference.Generation;
            _data[existingReference.Index] = (T)initialValue;

            OnComponentAdded?.Invoke(existingReference, typedInitValue);
        }

        public bool Destroy(DataReference reference)
        {
            if(IsValid(reference, out _) == false)
            {
                return false;
            }

            T valueBeforeRemoval = _data[reference.Index];
            _data[reference.Index] = default; //Technically unnecessary, but keeps things clean.
            _used[reference.Index] = false;

            OnComponentRemoved?.Invoke(reference, valueBeforeRemoval);

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
                reason = _generations[reference.Index] > reference.Generation
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

        public object? GetValueUntyped(DataReference reference)
        {
            return GetValue(reference);
        }

        private class ExceededDataTypeCapacityException : Exception
        {
            public ExceededDataTypeCapacityException(int maxCapacity )
                : base($"Exceeded max capacity of entries of type {typeof(T)}, which was set to {maxCapacity}.") { }
        }

        private class InvalidDataReferenceException : Exception
        {
            public InvalidDataReferenceException(DataReference reference, EInvalidReason reason)
                : base($"Passed reference for type {typeof(T)} was invalid. Reason: {reason}. Reference: {reference}") { }
        }

        private class InvalidDataReferenceAssignmentException : Exception
        {
            public InvalidDataReferenceAssignmentException(DataReference reference, EInvalidReason reason)
                : base($"Tried to create a {typeof(T)} value from a {nameof(DataReference)} that does not fit with existing " + 
                $"data structures. Reason: {reason}. Reference: {reference}.") { }
            
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
        TypeNotRegistered,
        IndexAlreadyAssigned
    }
}

