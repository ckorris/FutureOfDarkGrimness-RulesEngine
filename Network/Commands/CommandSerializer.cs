using FDG.Data.Commands;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FDG.Network.Commands
{
    internal class CommandSerializer
    {
        private readonly Dictionary<string, Type> _commandTypeRegistry = new Dictionary<string, Type>();

        public void RegisterCommandType<T>() where T : ICommand, new()
        {
            T instance = new T();
            string commandType = instance.CommandType;

            if (_commandTypeRegistry.ContainsKey(commandType) == false)
            {
                _commandTypeRegistry.Add(commandType, typeof(T));
            }
            else
            {
                throw new InvalidOperationException($"Command type {typeof(T)} was already registered.");
            }
        }

        public ArraySegment<byte> SerializeCommand(ICommand command)
        {
            //TODO: Avoid allocating the byte array, using a writer instead.
            string typeString = command.CommandType;
            int typeLength = Encoding.UTF8.GetByteCount(typeString);

            string json = JsonConvert.SerializeObject(command);
            int jsonLength = Encoding.UTF8.GetByteCount(json);

            int combinedLength = sizeof(int) + typeLength + jsonLength;

            byte[] commandArray = ArrayPool<byte>.Shared.Rent(combinedLength);

            int offset = 0;

            BitConverter.TryWriteBytes(commandArray.AsSpan(offset, sizeof(int)), typeLength);
            offset += sizeof(int);

            Encoding.UTF8.GetBytes(typeString, commandArray.AsSpan(offset, typeLength));
            offset += typeLength;

            //We don't need to store the length of the Json array because the array segment count holds that info.
            Encoding.UTF8.GetBytes(json, commandArray.AsSpan(offset, jsonLength));

            return new ArraySegment<byte>(commandArray, 0, combinedLength);
        }

        public IGameCommand DeserializeCommand(ArraySegment<byte> data)
        {
            int typeLength = BitConverter.ToInt32(data.Array.AsSpan(0, sizeof(int)));

            string typeString = Encoding.UTF8.GetString(data.Array, data.Offset + sizeof(int), typeLength);

            if(_commandTypeRegistry.ContainsKey(typeString) == false)
            {
                throw new InvalidOperationException($"Tried to deserialize unregistered type: {typeString}");
            }

            Type commandType = _commandTypeRegistry[typeString];

            int jsonOffset = sizeof(int) + typeLength;
            int jsonLength = data.Count - jsonOffset;

            string jsonString = Encoding.UTF8.GetString(data.Array, data.Offset + jsonOffset, jsonLength);
            //IGameCommand command = JsonConvert.DeserializeObject(jsonString, commandType) as IGameCommand;
            object? command = JsonConvert.DeserializeObject(jsonString, commandType);

            //Here I want to use command to invoke a typed delegate with it.

            return command as IGameCommand;
        }
    }
}
