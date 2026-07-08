using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;

namespace FDG.Network
{
    internal static class CommandProtocol
    {
        public const int MAGIC_NUMBERS = 06031989;
        public const int MAGIC_NUMBERS_BYTE_SIZE = 4;
        public const int HEADER_LENGTH_BYTE_SIZE = 4;

        // Upper bound on an inbound payload. The length prefix is attacker/corruption-controlled, so
        // without a cap one bad frame could make us rent a near-int.MaxValue buffer. Real frames
        // (full-state sync being the largest) stay well under this.
        public const int MAX_PAYLOAD_BYTES = 16 * 1024 * 1024; // 16 MB

        public const int TEMP_PORT = 6389; //TODO Make this specifyable.

        // Tunes a freshly connected/accepted socket for WAN turn-based play (QF3):
        // - NoDelay disables Nagle so a small decision frame isn't held waiting to coalesce, which with the
        //   peer's delayed-ACK can add ~100-200ms per message over the internet.
        // - KeepAlive (plus the per-connection time/interval tuning where the platform supports it) lets a
        //   silently-dead peer be detected instead of the connection looking alive forever.
        public static void ConfigureSocket(TcpClient client)
        {
            client.NoDelay = true;

            Socket socket = client.Client;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // Per-connection keepalive tuning isn't available on every OS/runtime combo; fall back to the
            // system defaults (KeepAlive still on) if any option is unsupported.
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
            }
            catch (SocketException) { }
            catch (PlatformNotSupportedException) { }
        }

        public static async Task WriteCommandAsync(Stream stream, ArraySegment<byte> dataBuffer,
            CancellationToken cancellationToken = default)
        {
            // Assemble the whole frame ([magic][length][payload]) into one buffer and write it in a single
            // WriteAsync (QF4). With NoDelay on, three separate small writes would each become their own tiny
            // segment; one write lets the frame ride in as few packets as possible. The per-connection write
            // lock upstream still serializes whole frames, so this can't interleave with another send.
            int frameLength = MAGIC_NUMBERS_BYTE_SIZE + HEADER_LENGTH_BYTE_SIZE + dataBuffer.Count;
            byte[] frame = ArrayPool<byte>.Shared.Rent(frameLength);
            try
            {
                BitConverter.TryWriteBytes(frame.AsSpan(0, MAGIC_NUMBERS_BYTE_SIZE), MAGIC_NUMBERS);
                BitConverter.TryWriteBytes(frame.AsSpan(MAGIC_NUMBERS_BYTE_SIZE, HEADER_LENGTH_BYTE_SIZE),
                    dataBuffer.Count);

                if (dataBuffer.Count > 0 && dataBuffer.Array != null)
                {
                    Buffer.BlockCopy(dataBuffer.Array, dataBuffer.Offset, frame,
                        MAGIC_NUMBERS_BYTE_SIZE + HEADER_LENGTH_BYTE_SIZE, dataBuffer.Count);
                }

                await stream.WriteAsync(frame.AsMemory(0, frameLength), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }

        public static async Task<ArraySegment<byte>> ReadCommandAsync(Stream stream,
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

            if (payloadLength < 0 || payloadLength > MAX_PAYLOAD_BYTES)
            {
                throw new IOException($"Received invalid payload length: {payloadLength} (must be 0..{MAX_PAYLOAD_BYTES}).");
            }

            byte[] payloadArray = ArrayPool<byte>.Shared.Rent(payloadLength); //Array may be too large.
            await stream.ReadExactlyAsync(payloadArray, 0, payloadLength, cancellationToken)
                .ConfigureAwait(false);

            ArraySegment<byte> payloadSegment = new ArraySegment<byte>(payloadArray, 0, payloadLength);

            return payloadSegment;
        }
    }
}
