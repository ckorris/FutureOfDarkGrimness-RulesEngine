using Newtonsoft.Json;
using NUnit.Framework.Constraints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network
{
    internal class WhitelistedTypeDeserializer
    {
        private readonly Dictionary<string, TypeAndCallbacks> _registry = new Dictionary<string, TypeAndCallbacks>();

        //public void RegisterType<T>(Action<T> onObjectDeserialized)
        public void RegisterType<T>(Func<T> onObjectDeserialized)
        {
            //Registers the type of T in some kind of collection that allows it to be serialized.
            //Also adds onObjectDeserialized to the same collection, so that whenever TryDeserializeObject
            //is called successfully with the type of T, onObjectDeserialized will be called with that object.
            //Also ideally we'll cache the reflection delegates, if required, to make things performant,
            //or take whatever other steps we should do instead.

            string? typeNameKey = typeof(T).FullName; //Convention that we'll use.

            if (typeNameKey == null)
            {
                throw new InvalidOperationException($"Cannot register a type with no FullName.");
            }

            if (_registry.TryGetValue(typeNameKey, out TypeAndCallbacks? typeAndCallbacks) == false)
            {
                typeAndCallbacks = new TypeAndCallbacks(typeof(T), new List<Delegate>());
                _registry[typeNameKey] = typeAndCallbacks;
            }
            typeAndCallbacks.callbacks.Add(onObjectDeserialized);
        }

        public bool TryDeserializeObject(string typeName, string objectJson)
        {
            //If the type indicated by typeName has been registered with RegisterType, it invokes all actions
            //registerd with it with the deserialized type, and returns true. Otherwise, returns false.
            //Deserialization should be done with Newtonsoft.Json. 
            if (_registry.TryGetValue(typeName, out TypeAndCallbacks? typeAndCallbacks) == false)
            {
                return false; //Nothing to find.
            }

            object? deserialized;


            deserialized = JsonConvert.DeserializeObject(objectJson, typeAndCallbacks.registeredType);

            //If we failed to deserialize, this is a bad error.
            if (deserialized == null)
            {
                throw new JsonSerializationException($"Failed to deserialize object of type {typeName}.");
            }

            foreach(Delegate callback in typeAndCallbacks.callbacks)
            {
                callback.DynamicInvoke(deserialized);
            }

            return true;
        }

        private record TypeAndCallbacks(Type registeredType, List<Delegate> callbacks);
    }
}
