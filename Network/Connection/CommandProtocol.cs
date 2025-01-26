using System;
using System.Buffers;
using System.Net.Sockets;

namespace FDG.Network
{
    internal static class CommandProtocol
    {
        public const int MAGIC_NUMBERS = 06031989;
        public const int MAGIC_NUMBERS_BYTE_SIZE = 4;
        public const int HEADER_LENGTH_BYTE_SIZE = 4;

        public const int TEMP_PORT = 6389; //TODO Make this specifyable.

        public static async Task WriteCommandAsync(NetworkStream stream, ArraySegment<byte> dataBuffer,
            CancellationToken cancellationToken = default)
        {
            //Write the magic numbers.
            byte[] magicNumbers = BitConverter.GetBytes(MAGIC_NUMBERS);
            await stream.WriteAsync(magicNumbers, 0, MAGIC_NUMBERS_BYTE_SIZE, cancellationToken)
                .ConfigureAwait(false);
            
            byte[] lengthPrefix = BitConverter.GetBytes(dataBuffer.Count);
            await stream.WriteAsync(lengthPrefix, 0, HEADER_LENGTH_BYTE_SIZE, cancellationToken)
                .ConfigureAwait(false);

            //TODO: Below I can auto-complete and use dataBuffer.Array.AsMemory to avoid warning. Research before using.
            await stream.WriteAsync(dataBuffer.Array, dataBuffer.Offset, dataBuffer.Count, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<ArraySegment<byte>> ReadCommandAsync(NetworkStream stream, 
            CancellationToken cancellationToken = default)
        {
            //Confirm magic numbers. Not strictly necessary, but early on, this will identify alignment issues immediately.
            byte[] magicNumbersBuffer = new byte[MAGIC_NUMBERS_BYTE_SIZE];

            await stream.ReadExactlyAsync(magicNumbersBuffer, 0, MAGIC_NUMBERS_BYTE_SIZE, cancellationToken)
                .ConfigureAwait(false);

            int receivedMagicNumbers = BitConverter.ToInt32(magicNumbersBuffer);
            if (receivedMagicNumbers != MAGIC_NUMBERS)
            {
                throw new IOException($"Header error in received message. Expected magic numbers, got: {receivedMagicNumbers}");
            }

            //Read prefix length.
            byte[] lengthBuffer = new byte[HEADER_LENGTH_BYTE_SIZE];
            await stream.ReadExactlyAsync(lengthBuffer, 0, HEADER_LENGTH_BYTE_SIZE, cancellationToken)
                .ConfigureAwait(false);

            int payloadLength = BitConverter.ToInt32(lengthBuffer);

            if (payloadLength < 0)
            {
                throw new IOException("Received invalid payload length: " + payloadLength);
            }

            byte[] payloadArray = ArrayPool<byte>.Shared.Rent(payloadLength); //Array may be too large.
            await stream.ReadExactlyAsync(payloadArray, 0, payloadLength, cancellationToken);

            ArraySegment<byte> payloadSegment = new ArraySegment<byte>(payloadArray, 0, payloadLength);

            return payloadSegment;
        }
    }
}
