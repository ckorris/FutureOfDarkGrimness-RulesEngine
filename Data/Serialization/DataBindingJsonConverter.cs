using Newtonsoft.Json;

namespace FDG.Data.Serialization
{
    //TODO: Implement this where it's serialized/deserialized, GameDataStore? And remove the old interface method.
    internal class DataBindingJsonConverter<T> : JsonConverter<DataBinding<T>>
    {
        private readonly IReadWriteableGameDataStore _gameDataStore;


        internal DataBindingJsonConverter(IReadWriteableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }

        public override DataBinding<T>? ReadJson(JsonReader reader, Type objectType, DataBinding<T>? existingValue, 
            bool hasExistingValue, JsonSerializer serializer)
        {
            if(reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            DataReference reference = serializer.Deserialize<DataReference>(reader);
            return _gameDataStore.GetDataBinding<T>(reference);
        }

        public override void WriteJson(JsonWriter writer, DataBinding<T>? value, JsonSerializer serializer)
        {
            if(value == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            serializer.Serialize(writer, value.Reference);
        }
    }
}
