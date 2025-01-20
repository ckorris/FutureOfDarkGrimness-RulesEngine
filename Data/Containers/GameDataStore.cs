using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FDG.Data
{
    public partial class GameDataStore : IReadableGameDataStore, IReadWriteableGameDataStore
    {
        private List<Type> _registeredTypes = new List<Type>() { typeof(UnreferenceableTypeStruct) };

        private Dictionary<TypeID, IComponentStore> _componentStores = new Dictionary<TypeID, IComponentStore>();

        private const int DEFAULT_COMPONENT_STORE_CAPACITY = 256;

        /// <summary>
        /// Creates a new instance with types mapped to IDs according to <paramref name="typeMap"/>. Use if 
        /// connecting to a host (where the type map should be sent over the network) or loading a save.
        /// </summary>
        /// <param name="typeMap">List of all types that should be registered, in the order of their corresponding IDs.</param>
        public static GameDataStore CreateFromTypeMap(List<Type> typeMap)
        {
            //ACTUALLY don't use, I want to make a builder class.
            throw new NotImplementedException();
        }

        private GameDataStore() { }

        private GameDataStore(List<TypeAndCapacity> typeMap)
        {
            ThrowIfTypeMapIsInvalid(typeMap);

            MethodInfo addComponentStoreInfo = typeof(GameDataStore).GetMethod(nameof(RegisterType), BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 1; i < typeMap.Count; i++)
            {
                MethodInfo genericAddComponentStoreInfo = addComponentStoreInfo.MakeGenericMethod(typeMap[i].Type);
                genericAddComponentStoreInfo.Invoke(this, [typeMap[i].Capacity]);
            }
        }



        /// <summary>
        /// Gets a list of all registered types that can be used to create a different instance of this
        /// with an identical type map.
        /// </summary>
        /// <returns></returns>
        public List<Type> GetTypeMap()
        {
            return new List<Type>(_registeredTypes);
        }

        /// <summary>
        /// Allows you to enter in values for <typeparamref name="T"/> to be stored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="capacity"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private TypeID RegisterType<T>(int capacity)
        {
            Type type = typeof(T); //Shorthand.
            if (_registeredTypes.FirstOrDefault(t => t == type) != default)
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

        public bool IsTypeAssigned<T>()
        {
            return _registeredTypes.IndexOf(typeof(T)) >= 0;
        }

        public DataReference Create<T>(T initialValue)
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            IComponentStore store = _componentStores[typeID];

            return ((ComponentStore<T>)store).Create(initialValue);
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

        public T GetValue<T>(DataReference reference)
        {
            GetTypeAndIDOrThrow<T>(out _, out TypeID typeID);
            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            return store.GetValue(reference);
        }

        public void SetValue<T>(DataReference reference, T value)
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            if(reference.TypeID != typeID)
            {
                throw new TypeMismatchException(type, reference.TypeID.ID, typeID.ID);
            }

            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            store.SetValue(reference, value);
        }

        public IEnumerable<T> GetAllValues<T>()
        {
            GetTypeAndIDOrThrow<T>(out _, out TypeID typeID);
            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            return store.GetAllValues();
        }

        public IEnumerable<DataReference> GetAllDataReferences<T>()
        {
            GetTypeAndIDOrThrow<T>(out _, out TypeID typeID);
            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            return store.GetAllDataReferences();
        }

        public void SubscribeToOnCreated<T>(Action<T> onCreated)
        {
            GetComponentStoreOrThrow<T>().OnComponentAdded += onCreated;
        }

        public void UnsubscribeFromOnCreated<T>(Action<T> onCreated)
        {
            GetComponentStoreOrThrow<T>().OnComponentAdded -= onCreated;
        }

        public void SubscribeToOnRemoved<T>(Action<T> onRemoved)
        {
            GetComponentStoreOrThrow<T>().OnComponentRemoved += onRemoved;
        }

        public void UnsubscribeFromOnRemoved<T>(Action<T> onRemoved)
        {
            GetComponentStoreOrThrow<T>().OnComponentRemoved -= onRemoved;
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

        private ComponentStore<T> GetComponentStoreOrThrow<T>()
        {
            GetTypeAndIDOrThrow<T>(out _, out TypeID typeID);

            if (_componentStores[typeID] != null)
            {
                return (ComponentStore<T>)_componentStores[typeID];
            }

            throw new NullReferenceException();
        }

        private void GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID)
        {
            type = typeof(T); //Shorthand.

            int typeIndex = _registeredTypes.IndexOf(type);
            if (typeIndex == -1)
            {
                throw new TypeNotRegisteredException(type);
            }

            typeID = new TypeID(typeIndex);
        }

        private static void ThrowIfTypeMapIsInvalid(List<TypeAndCapacity> typeMap)
        {
            //Make sure the placeholder exists. It's a big smell if it doesn't.
            if (typeMap.Count == 0 || typeMap[0].Type != typeof(UnreferenceableTypeStruct))
            {
                throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} that did not have its first index " +
                    $"set to type {nameof(UnreferenceableTypeStruct)}. This is enforced as the first item to avoid default values " +
                    "being set to a valid type, but likely means the list was generated incorrectly.");
            }

            //Check for duplicate types. 
            HashSet<Type> duplicateChecker = new HashSet<Type>();
            for (int i = 1; i < typeMap.Count; i++)
            {
                if (typeMap[i] == null)
                {
                    throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} with a null entry at index {i}.");
                }

                if (duplicateChecker.Add(typeMap[i].Type) == false)
                {
                    throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} with a duplicate type entry: {typeMap[i]}.");
                }
            }
        }


        /// <summary>
        /// Exists so that the index of any used type is not 0, so that a default TypeID doesn't erroneously
        /// point to a valid type, causing bugs to be harder to find.
        /// </summary>
        private struct UnreferenceableTypeStruct { } 
    }
}
