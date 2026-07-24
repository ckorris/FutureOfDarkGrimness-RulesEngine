using FDG.Data.Containers;
using FDG.Data.Serialization;
using FDG.SaveLoad;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace FDG.Data
{
    public partial class GameDataStore : IReadableGameDataStore, IReadWriteableGameDataStore
    {
        private List<Type> _registeredTypes = new List<Type>() { typeof(UnreferenceableTypeStruct) };

        private Dictionary<TypeID, IComponentStore> _componentStores = new Dictionary<TypeID, IComponentStore>();

        public event Action<DataReference, string>? OnDataAddedAsJson;
        public event Action<DataReference, string>? OnDataUpdatedAsJson;
        public event Action<DataReference>? OnDataRemoved;

        private const int DEFAULT_COMPONENT_STORE_CAPACITY = 256;

        private JsonConverter[] _jsonConverters;

        private JsonSerializerSettings _jsonConvertSettings;

        /// <summary>
        /// Creates a new instance with types mapped to IDs according to <paramref name="typeMap"/>. Use if 
        /// connecting to a host (where the type map should be sent over the network) or loading a save.
        /// </summary>
        /// <param name="typeMap">List of all types that should be registered, in the order of their corresponding IDs.</param>
        public static GameDataStore CreateFromTypeMap(List<Type> typeMap)
        {
            // No per-type capacities available, so size every store to the default. Adequate when
            // the data is known to fit; prefer the TypeAndCapacity overload when loading a save,
            // where the original capacities are recorded (component stores do not grow).
            List<TypeAndCapacity> withCapacities = new List<TypeAndCapacity>(typeMap.Count);
            foreach (Type type in typeMap)
            {
                withCapacities.Add(new TypeAndCapacity(type, DEFAULT_COMPONENT_STORE_CAPACITY));
            }

            return new GameDataStore(withCapacities);
        }

        /// <summary>
        /// Creates a store whose type map (and per-type capacities) exactly match those captured by
        /// <see cref="GetTypeMapWithCapacities"/>. Used when loading a save: the recorded capacities
        /// guarantee every saved <see cref="DataReference"/> index fits, since component stores are
        /// fixed-size.
        /// </summary>
        public static GameDataStore CreateFromTypeMap(List<TypeAndCapacity> typeMap)
        {
            return new GameDataStore(typeMap);
        }

        private GameDataStore(List<TypeAndCapacity> typeMap)
        {
            ThrowIfTypeMapIsInvalid(typeMap);


            MethodInfo addComponentStoreInfo = typeof(GameDataStore).GetMethod(nameof(RegisterType), BindingFlags.NonPublic | BindingFlags.Instance);

            _jsonConverters = new JsonConverter[typeMap.Count - 1];

            for (int i = 1; i < typeMap.Count; i++)
            {
                MethodInfo genericAddComponentStoreInfo = addComponentStoreInfo.MakeGenericMethod(typeMap[i].Type);
                genericAddComponentStoreInfo.Invoke(this, [typeMap[i].Capacity]);

                //Create a DataBindingConverter for each.
                Type converterGenericType = typeof(DataBindingJsonConverter<>).MakeGenericType(typeMap[i].Type);
                object converterInstance = Activator.CreateInstance(converterGenericType, [this as IReadWriteableGameDataStore]);
                _jsonConverters[i - 1] = (JsonConverter)converterInstance;
            }

             // Enums ride the wire (and store-value blobs) as their member names, not ordinals, so that
             // reordering an enum's members stops being a silent wire/save format change (#075). The
             // StringEnumConverter reads both string and integer tokens, so older int-encoded payloads
             // still load.
             List<JsonConverter> converters = _jsonConverters.ToList();
             converters.Add(new StringEnumConverter());

             _jsonConvertSettings = new JsonSerializerSettings()
             {
                 TypeNameHandling = TypeNameHandling.Auto,
                 // Write polymorphic $type payloads (base shapes, token payloads/clear-triggers, terrain
                 // zones) by stable string ID rather than assembly-qualified name so a rename doesn't break
                 // saves or the full-state sync (#070). On READ this same binder now ALSO enforces the
                 // untrusted-input allowlist (#265): store rebuild feeds both save/load and the network
                 // full-state sync, so a crafted save file or a hostile peer's snapshot can't resolve a
                 // Newtonsoft gadget $type here. Only engine types / registered IDs / benign collections
                 // resolve; anything else throws.
                 SerializationBinder = new AllowlistSerializationBinder(),
                 Converters = converters
             };

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
        /// A stable hash of the registered type map (each type's <see cref="Type.FullName"/>, in TypeID
        /// order). Two stores built from the same registration order produce the same fingerprint; adding,
        /// removing, renaming, or reordering a registered type changes it. Used by the network join
        /// handshake (#075) to detect that a connecting client was built against a different store shape —
        /// since the TypeID is positional and baked into every serialized <see cref="DataReference"/>, a
        /// drifted type map would silently corrupt replicated data. Uses SHA-256 (not
        /// <see cref="object.GetHashCode"/>, which is per-process randomized for strings) so the value is
        /// stable across processes and machines.
        /// </summary>
        public string GetTypeMapFingerprint()
        {
            StringBuilder builder = new StringBuilder();
            foreach (Type type in _registeredTypes)
            {
                builder.Append(type.FullName ?? type.Name);
                builder.Append('\n');
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// The full type map paired with each type's component-store capacity, in TypeID order.
        /// Sufficient to recreate an identically-shaped store via
        /// <see cref="CreateFromTypeMap(List{TypeAndCapacity})"/>. The placeholder type at index 0
        /// has no component store and reports capacity 0.
        /// </summary>
        public List<TypeAndCapacity> GetTypeMapWithCapacities()
        {
            List<TypeAndCapacity> result = new List<TypeAndCapacity>(_registeredTypes.Count);
            for (int i = 0; i < _registeredTypes.Count; i++)
            {
                int capacity = _componentStores.TryGetValue(new TypeID(i), out IComponentStore store)
                    ? store.Capacity
                    : 0;
                result.Add(new TypeAndCapacity(_registeredTypes[i], capacity));
            }

            return result;
        }

        // NOTE (#186): these settings carry the PERMISSIVE binder (unregistered $type falls back to
        // assembly-qualified resolution) and are safe only for trusted input: saves and store blobs.
        // The network path must not use them directly — MessageSerializer clones them and swaps in
        // AllowlistSerializationBinder. If a new setting is added here, mirror it in that clone.
        public JsonSerializerSettings GetJsonSettings()
        {
            return _jsonConvertSettings;
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

            if (capacity <= 0)
            {
                capacity = DEFAULT_COMPONENT_STORE_CAPACITY;
            }

            var newComponentStore = new ComponentStore<T>(capacity, typeID);
            newComponentStore.OnComponentAdded += InvokeDataAddedUntyped;
            newComponentStore.OnAnyUpdatedTyped += InvokeDataUpdatedUntyped;
            newComponentStore.OnComponentRemoved += InvokeDataRemovedUntyped;

            _componentStores.Add(typeID, newComponentStore);
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

        public void CreateFromReferenceAndJson(DataReference reference, string initValueAsJson)
        {
            IComponentStore store = _componentStores[reference.TypeID];
            store.CreateFromReference(reference, DeserializeForReference(reference, initValueAsJson));
        }

        /// <summary>
        /// Snapshot-rebuild counterpart of <see cref="CreateFromReferenceAndJson"/> (#270): same
        /// deserialization, but the entry's generation is adopted rather than required to follow on from
        /// this store's, so a recycled slot replays. Used by <see cref="FDG.SaveLoad.StoreReplay"/>.
        /// </summary>
        public void CreateFromReplayJson(DataReference reference, string initValueAsJson)
        {
            IComponentStore store = _componentStores[reference.TypeID];
            store.CreateFromReplay(reference, DeserializeForReference(reference, initValueAsJson));
        }

        private object DeserializeForReference(DataReference reference, string initValueAsJson)
        {
            Type objectType = _registeredTypes[reference.TypeID.ID];
            object? deserializedValue = JsonConvert.DeserializeObject(initValueAsJson, objectType, _jsonConvertSettings);

            if (deserializedValue == null)
            {
                throw new ArgumentException($"Passed in Json could not be deserialized.");
            }

            return deserializedValue;
        }

        public bool Destroy(DataReference reference)
        {
            if (_componentStores.ContainsKey(reference.TypeID) == false)
            {
                return false;
            }

            return _componentStores[reference.TypeID].Destroy(reference);
        }

        public DataBinding<T> GetDataBinding<T>(DataReference dataReference)
        {
            return GetComponentStoreOrThrow<T>().GetDataBinding(dataReference);
        }

        public IEnumerable<DataBinding<T>> GetAllDataBindings<T>()
        {
            GetTypeAndIDOrThrow<T>(out _, out TypeID typeID);
            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            foreach (DataReference reference in store.GetAllDataReferences())
            {
                yield return store.GetDataBinding(reference);
            }
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

        public string GetValueAsJson<T>(DataReference reference)
        {
            T value = GetValue<T>(reference);
            string valueAsJson = JsonConvert.SerializeObject(value, _jsonConvertSettings);
            return valueAsJson;
        }

        public void SetValue<T>(DataReference reference, T value)
        {
            GetTypeAndIDOrThrow<T>(out Type type, out TypeID typeID);

            if (reference.TypeID != typeID)
            {
                throw new TypeMismatchException(type, reference.TypeID.ID, typeID.ID);
            }

            ComponentStore<T> store = (ComponentStore<T>)_componentStores[typeID];

            store.SetValue(reference, value);
        }

        public void SetValueWithJson(DataReference reference, string json)
        {
            Type objectType = _registeredTypes[reference.TypeID.ID];

            object? deserializedValue = JsonConvert.DeserializeObject(json, objectType, _jsonConvertSettings);

            if (deserializedValue == null)
            {
                throw new ArgumentException($"Passed in Json could not be deserialized.");
            }

            IComponentStore untypedComponentStore = _componentStores[reference.TypeID];

            untypedComponentStore.SetValue(reference, deserializedValue);
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

        public List<ReferenceJsonValuePair> GetAllDataReferencesAsJson()
        {
            List<ReferenceJsonValuePair> allReferences = new List<ReferenceJsonValuePair>();

            foreach (IComponentStore componentStore in _componentStores.Values)
            {
                foreach (DataReference reference in componentStore.GetAllDataReferences())
                {
                    object? value = componentStore.GetValueUntyped(reference);
                    string valueAsJson = JsonConvert.SerializeObject(value, _jsonConvertSettings);
                    allReferences.Add(new ReferenceJsonValuePair(reference, valueAsJson));
                }
            }

            return allReferences;
        }

        public void SubscribeToOnCreated<T>(Action<DataReference, T> onCreated)
        {
            GetComponentStoreOrThrow<T>().OnComponentAdded += onCreated;
        }

        public void UnsubscribeFromOnCreated<T>(Action<DataReference, T> onCreated)
        {
            GetComponentStoreOrThrow<T>().OnComponentAdded -= onCreated;
        }
        public void SubscribeToOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated)
        {
            GetComponentStoreOrThrow<T>().OnAnyUpdatedTyped += onUpdated;
        }

        public void UnsubscribeFromOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated)
        {
            GetComponentStoreOrThrow<T>().OnAnyUpdatedTyped -= onUpdated;
        }

        public void SubscribeToOnRemoved<T>(Action<DataReference, T> onRemoved)
        {
            GetComponentStoreOrThrow<T>().OnComponentRemoved += onRemoved;
        }

        public void UnsubscribeFromOnRemoved<T>(Action<DataReference, T> onRemoved)
        {
            GetComponentStoreOrThrow<T>().OnComponentRemoved -= onRemoved;
        }

        private void InvokeDataAddedUntyped<T>(DataReference dataReference, T typedValue)
        {
            if (typedValue == null)
            {
                throw new NullReferenceException();
            }
            if (OnDataAddedAsJson != null)
            {
                string asJson = JsonConvert.SerializeObject(typedValue, _jsonConvertSettings);
                OnDataAddedAsJson.Invoke(dataReference, asJson);
            }
        }

        private void InvokeDataUpdatedUntyped<T>(DataReference dataReference, T typedValue)
        {
            if (typedValue == null)
            {
                throw new NullReferenceException();
            }

            if (OnDataUpdatedAsJson != null)
            {
                string asJson = JsonConvert.SerializeObject(typedValue, _jsonConvertSettings);
                OnDataUpdatedAsJson.Invoke(dataReference, asJson);
            }
        }

        private void InvokeDataRemovedUntyped<T>(DataReference dataReference, T _)
        {
            OnDataRemoved?.Invoke(dataReference);
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
                      $"{nameof(DataReference)} object with type index {providedIndex} when the correct index is {realIndex}.")
            { }
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
        /// The type occupying TypeID 0 (see <see cref="UnreferenceableTypeStruct"/>). Exposed so the save
        /// type-ID registry (#070) can give it a stable ID like every other registered type, without
        /// leaking the private struct.
        /// </summary>
        public static Type PlaceholderType => typeof(UnreferenceableTypeStruct);

        /// <summary>
        /// Exists so that the index of any used type is not 0, so that a default TypeID doesn't erroneously
        /// point to a valid type, causing bugs to be harder to find.
        /// </summary>
        private struct UnreferenceableTypeStruct { }
    }
}
