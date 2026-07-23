using FDG.Data;
using Newtonsoft.Json;
using System.Buffers;
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

        //Unknown wire types we've already warned about, so version skew logs once per type instead of per message.
        private readonly HashSet<string> _warnedUnknownTypes = new HashSet<string>();

        private readonly ITextOutput _textOutput;

        private JsonSerializerSettings _settings;

        public MessageSerializer(IReadableGameDataStore gameDataStore, ITextOutput? textOutput = null)
        {
            // The wire uses the store's settings (same converters, same TypeNameHandling — the
            // full-state sync depends on that) but with the allowlist binder swapped in (#186): a
            // received $type may only resolve to a registered stable ID, an engine type, or a
            // benign collection thereof. Never hand gameDataStore.GetJsonSettings() to the wire
            // directly — its StableTypeSerializationBinder falls back to resolving arbitrary
            // assembly-qualified names, which on attacker-controlled input is the classic
            // Newtonsoft deserialization RCE. (If GetJsonSettings ever gains a new setting,
            // mirror it here.)
            JsonSerializerSettings storeSettings = gameDataStore.GetJsonSettings();
            _settings = new JsonSerializerSettings()
            {
                TypeNameHandling = storeSettings.TypeNameHandling,
                Converters = storeSettings.Converters,
                SerializationBinder = new WireSerializationBinder(),
            };
            _textOutput = textOutput ?? new ConsoleTextOutput();
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
                //Tolerating types this endpoint doesn't handle is intentional (not everyone cares about every
                //message), but an *unknown* type usually means version skew — surface it visibly, once per type,
                //rather than vanishing into Debug output that's compiled out of Release builds.
                if (_warnedUnknownTypes.Add(typeString))
                {
                    _textOutput.Log($"Received unregistered message type {typeString}. Discarding (possible version mismatch). Further instances suppressed.");
                }
                return null;
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
