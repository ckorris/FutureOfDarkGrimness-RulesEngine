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
        /// Creates a new instance  with no types assigned. Do not use on a client or if loading
        /// a save. Wait to be sent a type map and use <see cref="CreateFromTypeMap(List{Type})"/> instead.
        /// </summary>
        public static GameDataStore CreateEmpty()
        {
            return new GameDataStore();
        }

        /// <summary>
        /// Creates a new instance with types mapped to IDs according to <paramref name="typeMap"/>. Use if 
        /// connecting to a host (where the type map should be sent over the network) or loading a save.
        /// </summary>
        /// <param name="typeMap">List of all types that should be registered, in the order of their corresponding IDs.</param>
        public static GameDataStore CreateFromTypeMap(List<Type> typeMap)
        {
            return new GameDataStore(typeMap);
        }

        private GameDataStore() { }

        private GameDataStore(List<Type> typeMap)
        {
            //Make sure the placeholder exists. It's a big smell if it doesn't.
            if(typeMap.Count == 0 || typeMap[0] != typeof(UnreferenceableTypeStruct))
            {
                throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} that did not have its first index " + 
                    $"set to type {nameof(UnreferenceableTypeStruct)}. This is enforced as the first item to avoid default values " + 
                    "being set to a valid type, but likely means the list was generated incorrectly.");
            }

            //Check for duplicate types. 
            HashSet<Type> duplicateChecker = new HashSet<Type>();
            for(int i = 1; i < typeMap.Count; i++)
            {
                if (typeMap[i] == null)
                {
                    throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} with a null entry at index {i}.");
                }

                if(duplicateChecker.Add(typeMap[i]) == false)
                {
                    throw new ArgumentException($"Tried to create a {nameof(GameDataStore)} with a duplicate type entry: {typeMap[i]}.");
                }
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
            return _registeredTypes.IndexOf(typeof(T)) >= 0;
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
