using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    /// <summary>
    /// TODO: This should be internal but I'm prototyping how to use this with a network connection.
    /// </summary>
    public class MessageSerializer
    {
        private readonly Dictionary<string, Type> _messageTypeRegistry = new Dictionary<string, Type>();

        private readonly Dictionary<Type, List<Delegate>> _messageHandlers = new Dictionary<Type, List<Delegate>>();

        public void RegisterMessageType<T>()
        {
            string messageType = typeof(T).ToString();

            if (_messageTypeRegistry.ContainsKey(messageType) == false)
            {
                _messageTypeRegistry.Add(messageType, typeof(T));
            }
            else
            {
                throw new InvalidOperationException($"Message type {typeof(T)} was already registered.");
            }
        }

        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived)
        {
            Type typeKey = typeof(T);
            if (_messageHandlers.TryGetValue(typeKey, out List<Delegate>? handlers) == false)
            {
                handlers = new List<Delegate>();
                _messageHandlers[typeKey] = handlers;
            }

            handlers.Add(onMessageReceived);
        }

        /// <summary>
        /// Serializes a message into a custom byte format:
        /// [4-byte length of type string][UTF-8 type string][UTF-8 JSON]
        /// </summary>
        public ArraySegment<byte> SerializeMessage<T>(T message)
        {
            string typeString = typeof(T).ToString();
            int typeLength = Encoding.UTF8.GetByteCount(typeString);

            string json = JsonConvert.SerializeObject(message);
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

        public void DeserializeMessageAndInvoke(ArraySegment<byte> data)
        {
            int typeLength = BitConverter.ToInt32(data.Array.AsSpan(0, sizeof(int)));

            string typeString = Encoding.UTF8.GetString(data.Array, data.Offset + sizeof(int), typeLength);

            if(_messageTypeRegistry.ContainsKey(typeString) == false)
            {
                throw new InvalidOperationException($"Tried to deserialize unregistered type: {typeString}");
            }

            Type messageType = _messageTypeRegistry[typeString];

            int jsonOffset = sizeof(int) + typeLength;
            int jsonLength = data.Count - jsonOffset;

            string jsonString = Encoding.UTF8.GetString(data.Array, data.Offset + jsonOffset, jsonLength);
            object message = JsonConvert.DeserializeObject(jsonString, messageType);

            DispatchToHandlers(message);
        }

        private void DispatchToHandlers(object messageObject)
        {
            Type actualType = messageObject.GetType();
            if(_messageHandlers.TryGetValue(actualType, out List<Delegate>? handlers))
            {
                Debug.WriteLine($"Handlers: {handlers.Count}");

                foreach(Delegate del in handlers)
                {
                    del.DynamicInvoke(messageObject);
                }
            }
        }
    }
}
