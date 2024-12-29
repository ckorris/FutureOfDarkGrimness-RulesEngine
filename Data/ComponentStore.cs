using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Data
{
    public interface IComponentStore
    {
        DataReference Create();
        bool Destroy(DataReference reference);

        bool IsValid(DataReference reference, out EInvalidReason reason);
    }

    public class ComponentStore<T> : IComponentStore
        where T : struct
    {
        private T[] _data;
        private bool[] _used;
        private int[] _generations;
        private int _capacity;

        private TypeID _typeID;

        public ComponentStore(int capacity, TypeID typeID)
        {
            _capacity = capacity;
            _data = new T[capacity];
            _used = new bool[capacity];
            _generations = new int[capacity];
            _typeID = typeID;
        }

        public DataReference Create()
        {
            for (int i = 0; i < _capacity; i++)
            {
                if( _used[i] == false)
                {
                    _used[i] = true;
                    _generations[i]++;

                    return new DataReference()
                    { 
                        TypeID = _typeID,
                        Index = i,
                        Generation = _generations[i]
                    };
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
            if (IsValid(reference, out EInvalidReason failReason) == false)
            {
                throw new InvalidDataReferenceException(reference, failReason);
            }

            _data[reference.Index] = value;
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
            if (_generations[reference.Index] != reference.Index)
            {
                reason = _generations[reference.Index] < reference.Index
                    ? EInvalidReason.OutdatedGeneration
                    : EInvalidReason.FutureGeneration;
                return false;
            }

            reason = EInvalidReason.Valid;
            return true;
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

