using FDG.Data;
using Newtonsoft.Json;
using System.Buffers;
using System.Diagnostics;
using System.Text;


namespace FDG.Network.Messages
{
    internal interface IMessageSerializer
    {
        public void RegisterMessageType<T>();

        public ArraySegment<byte> SerializeMessage<T>(T message);

        public object? DeserializeMessage(ArraySegment<byte> data);
    }

    /// <summary>
    /// TODO: This should be internal but I'm prototyping how to use this with a network connection.
    /// </summary>
    internal class MessageSerializer : IMessageSerializer
    {
        private readonly Dictionary<string, Type> _messageTypeRegistry = new Dictionary<string, Type>();

        private JsonSerializerSettings _settings;

        /*
        private static JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Auto,
        };
        */

        public MessageSerializer(IReadableGameDataStore gameDataStore)
        {
            _settings = gameDataStore.GetJsonSettings();
        }

        public void RegisterMessageType<T>()
        {
            string messageType = typeof(T).ToString();

            if (_messageTypeRegistry.ContainsKey(messageType) == false)
            {
                _messageTypeRegistry.Add(messageType, typeof(T));
            }
        }


        /// <summary>
        /// Serializes a message into a custom byte format:
        /// [4-byte length of type string][UTF-8 type string][UTF-8 JSON]
        /// </summary>
        public ArraySegment<byte> SerializeMessage<T>(T message)
        {
            string typeString = typeof(T).ToString();
            int typeLength = Encoding.UTF8.GetByteCount(typeString);

            string json = JsonConvert.SerializeObject(message, _settings);
            int jsonLength = Encoding.UTF8.GetByteCount(json);

            int combinedLength = sizeof(int) + typeLength + jsonLength;

            byte[] messageArray = ArrayPool<byte>.Shared.Rent(combinedLength);

            int offset = 0;

            BitConverter.TryWriteBytes(messageArray.AsSpan(offset, sizeof(int)), typeLength);
            offset += sizeof(int);

            Encoding.UTF8.GetBytes(typeString, messageArray.AsSpan(offset, typeLength));
            offset += typeLength;

            //We don't need to store the length of the Json array because the array segment count holds that info.
            Encoding.UTF8.GetBytes(json, messageArray.AsSpan(offset, jsonLength));

            return new ArraySegment<byte>(messageArray, 0, combinedLength);
        }



        public object? DeserializeMessage(ArraySegment<byte> data)
        {
            int typeLength = BitConverter.ToInt32(data.Array.AsSpan(data.Offset, sizeof(int)));

            string typeString = Encoding.UTF8.GetString(data.Array, data.Offset + sizeof(int), typeLength);

            if(_messageTypeRegistry.ContainsKey(typeString) == false)
            {
                //This should be okay, not everyone cares about every message.
                Debug.WriteLine($"Received unregistered message type {typeString}. Discarding.");
                return null;
                //throw new InvalidOperationException($"Tried to deserialize unregistered type: {typeString}");
            }

            Type messageType = _messageTypeRegistry[typeString];

            int jsonOffset = sizeof(int) + typeLength;
            int jsonLength = data.Count - jsonOffset;

            string jsonString = Encoding.UTF8.GetString(data.Array, data.Offset + jsonOffset, jsonLength);

            object? message = JsonConvert.DeserializeObject(jsonString, messageType, _settings);

            return message;

            //DispatchToHandlers(message, connectionID);
        }
    }
}
