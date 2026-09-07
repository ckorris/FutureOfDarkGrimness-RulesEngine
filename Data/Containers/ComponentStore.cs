
using FDG.Data.Containers;

namespace FDG.Data
{
    public interface IComponentStore
    {
        int Capacity { get; }

        bool IsValid(DataReference reference, out EInvalidReason reason);

        void CreateFromReference(DataReference existingReference, object initialValue);

        /// <summary>Whole-store-snapshot counterpart of <see cref="CreateFromReference"/>; adopts the
        /// entry's generation instead of requiring it to follow on from this store's (#270).</summary>
        void CreateFromReplay(DataReference existingReference, object initialValue);

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

        /// <summary>
        /// Creates at a foreign reference from the LIVE incremental stream (a network add message),
        /// where this store is expected to be in step with the sender: the generation must be exactly
        /// the next one for that slot, and anything else means a message was missed or arrived out of
        /// order. Rebuilding a whole store from a snapshot goes through
        /// <see cref="CreateFromReplay"/> instead, which has no such expectation.
        /// </summary>
        public void CreateFromReference(DataReference existingReference, object initialValue)
        {
            T typedInitValue = ValidateForeignCreate(existingReference, initialValue);

            //The live stream is sequential, so the sender's generation must be exactly one ahead of
            //ours: further ahead means we missed a create, at-or-behind means this is a stale message.
            if (_generations[existingReference.Index] < existingReference.Generation - 1)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.FutureGeneration);
            }

            if(_generations[existingReference.Index] >= existingReference.Generation)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.OutdatedGeneration);
            }

            Occupy(existingReference, typedInitValue);
        }

        /// <summary>
        /// Creates at a foreign reference while REBUILDING this store from a whole-store snapshot (a
        /// save file, or the join-time catch-up sync) - see <see cref="FDG.SaveLoad.StoreReplay"/>.
        ///
        /// <para>#270: unlike the live path above, this adopts whatever generation the entry carries
        /// instead of requiring it to follow on from ours. A slot that was recycled during the session
        /// (destroyed, then refilled by <see cref="Create"/>) is at generation 2 or more, while a store
        /// rebuilt from scratch starts every slot at 0 - so demanding "exactly one ahead" rejected the
        /// entry as <see cref="EInvalidReason.FutureGeneration"/> and made the whole snapshot
        /// unloadable. The generation is a stale-reference guard between a live sender and receiver; it
        /// says nothing about a snapshot, whose references are internally consistent by construction
        /// (every binding pointing at that slot carries the same generation). Adopting it preserves
        /// those bindings, which is exactly what a faithful rebuild needs.</para>
        ///
        /// <para>The invariants that DO mean something here are kept: a real index, the right type, a
        /// slot not already filled by this same replay, and a generation from a live
        /// <see cref="Create"/> (which pre-increments, so 1 or more - 0 is an unset/null reference).</para>
        /// </summary>
        public void CreateFromReplay(DataReference existingReference, object initialValue)
        {
            T typedInitValue = ValidateForeignCreate(existingReference, initialValue);

            if (existingReference.Generation < 1)
            {
                throw new InvalidDataReferenceAssignmentException(existingReference, EInvalidReason.OutdatedGeneration);
            }

            Occupy(existingReference, typedInitValue);
        }

        // The checks both foreign-create paths share, plus the cast. Grows to fit first: a high index is
        // fine — these paths replay references whose source store may have grown past ours. A negative
        // index can never be valid, and EnsureCapacity throws if the index is implausibly large.
        private T ValidateForeignCreate(DataReference existingReference, object initialValue)
        {
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

            if (initialValue is not T typedInitValue)
            {
                throw new ArgumentException($"Passed in an object to {typeof(ComponentStore<T>)} that could not be " +
                    $"converted to a {typeof(T)}.");
            }

            return typedInitValue;
        }

        private void Occupy(DataReference reference, T value)
        {
            _used[reference.Index] = true;
            _generations[reference.Index] = reference.Generation;
            _data[reference.Index] = value;

            OnComponentAdded?.Invoke(reference, value);
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

        /// <summary>
        /// A binding for a slot that may not be occupied YET (#396). The whole-store clone
        /// (<see cref="FDG.SaveLoad.StoreClone"/>) rebuilds entries in registration order, and a
        /// ModelData's facing binding points into the Float2 store registered after it - exactly the
        /// forward reference <see cref="FDG.SaveLoad.StoreReplay"/> retries around. The binding is the
        /// same object every later <see cref="GetDataBinding"/> for that slot returns, so holders share
        /// it as they do after a JSON replay; a read through it before the slot is replayed throws like
        /// any other invalid reference.
        /// </summary>
        internal DataBinding<T> BindForReplay(DataReference reference)
        {
            if (reference.TypeID != _typeID)
            {
                throw new InvalidDataReferenceAssignmentException(reference, EInvalidReason.IncorrectType);
            }

            if (_bindings.TryGetValue(reference.Index, out DataBinding<T>? existing))
            {
                return existing;
            }

            DataBinding<T> binding = new DataBinding<T>(reference, this);
            _bindings.Add(reference.Index, binding);
            return binding;
        }

        /// <summary>
        /// Fills this (fresh) store with <paramref name="source"/>'s occupied slots, each value passed
        /// through <paramref name="cloneValue"/>, adopting the source generations the way
        /// <see cref="CreateFromReplay"/> does (#396). Free slots stay at generation 0 - what a JSON
        /// replay leaves them at, since a save only records occupied slots - so a later Create hands
        /// out the same reference on either path. No events: nothing can be subscribed to a store that
        /// is still being built. Reads the source only, so any number of clones may be taken from one
        /// source concurrently.
        /// </summary>
        internal void ReplayFrom(ComponentStore<T> source, Func<T, T> cloneValue)
        {
            EnsureCapacity(source._capacity);
            for (int i = 0; i < source._capacity; i++)
            {
                if (source._used[i] == false)
                {
                    continue;
                }

                if (_used[i])
                {
                    throw new InvalidDataReferenceAssignmentException(
                        new DataReference { TypeID = _typeID, Index = i, Generation = source._generations[i] },
                        EInvalidReason.IndexAlreadyAssigned);
                }

                _used[i] = true;
                _generations[i] = source._generations[i];
                _data[i] = cloneValue(source._data[i]);
            }
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

