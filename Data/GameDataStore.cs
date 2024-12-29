using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Data
{
    public class GameDataStore
    {
        private List<Type> _registeredTypes = new List<Type>() { typeof(UnreferenceableTypeStruct) };

        private Dictionary<TypeID, IComponentStore> _componentStores = new Dictionary<TypeID, IComponentStore>();

        private const int DEFAULT_COMPONENT_STORE_CAPACITY = 256;

        /// <summary>
        /// Allows you to enter in values for <typeparamref name="T"/> to be stored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="capacity"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public TypeID RegisterType<T>(int capacity) where T : struct
        {
            Type type = typeof(T); //Shorthand.
            if (_registeredTypes.FirstOrDefault(type) != default)
            {
                throw new ArgumentException($"Tried to register type {type} but it was already registered.");
            }

            _registeredTypes.Add(type);

            TypeID typeID = new TypeID(_registeredTypes.Count - 1);

            if(capacity <= 0)
            {
                capacity = DEFAULT_COMPONENT_STORE_CAPACITY;
            }

            _componentStores.Add(typeID, new ComponentStore<T>(capacity, typeID));
            return typeID;
        }

        public bool IsTypeAssigned<T>() where T : struct
        {
            return _registeredTypes.IndexOf(typeof(T)) > 0;
        }

        public DataReference Create<T>() where T : struct
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            IComponentStore store = _componentStores[typeID];

            return store.Create();
        }

        public bool Destroy(DataReference reference)
        {
            if (_componentStores.ContainsKey(reference.TypeID) == false)
            {
                return false;
            }

            return _componentStores[reference.TypeID].Destroy(reference);
        }

        public bool IsValid(DataReference reference, out EInvalidReason failReason)
        {
            if (_componentStores.ContainsKey(reference.TypeID) == false)
            {
                failReason = EInvalidReason.TypeNotRegistered;
                return false;
            }

            return _componentStores[reference.TypeID].IsValid(reference, out failReason);
        }

        public T GetValue<T>(DataReference reference) where T : struct
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            return store.GetValue(reference);
        }

        public void SetValue<T>(DataReference reference, T value) where T : struct
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            if(reference.TypeID != typeID)
            {
                throw new TypeMismatchException(type, reference.TypeID.ID, typeID.ID);
            }

            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            store.SetValue(reference, value);
        }

        private class TypeNotRegisteredException : Exception
        {
            public TypeNotRegisteredException(Type type)
                : base($"Type of {type} not registered within {nameof(GameDataStore)}.") { }
        }

        private class TypeMismatchException : Exception
        {
            public TypeMismatchException(Type providedType, int providedIndex, int realIndex)
                : base($"Tried to access value in {nameof(GameDataStore)} of type {providedType}, but passed in a " + 
                      $"{nameof(DataReference)} object with type index {providedIndex} when the correct index is {realIndex}.") { }
        }

        private void GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID)
            where T : struct
        {
            type = typeof(T); //Shorthand.

            int typeIndex = _registeredTypes.IndexOf(type);
            if (typeIndex == -1)
            {
                throw new TypeNotRegisteredException(type);
            }

            typeID = new TypeID(typeIndex);
        }

        /// <summary>
        /// Exists so that the index of any used type is not 0, so that a default TypeID doesn't erroneously
        /// point to a valid type, causing bugs to be harder to find.
        /// </summary>
        private struct UnreferenceableTypeStruct { } 
    }
}
