using System;
using System.IO;
using System.Threading.Tasks;
using FDG.Network;
using NUnit.Framework;

namespace FDG.Tests
{
    // #078 — CommandProtocol read hardening (audit §13.25). The length prefix is corruption/attacker
    // controlled; without an upper bound one bad frame could rent a near-int.MaxValue buffer.
    [TestFixture]
    public class CommandProtocolTests
    {
        private static MemoryStream FrameWith(int magic, int declaredLength, byte[] payload)
        {
            var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(magic));
            ms.Write(BitConverter.GetBytes(declaredLength));
            ms.Write(payload);
            ms.Position = 0;
            return ms;
        }

        [Test]
        public async Task WriteThenRead_RoundTripsPayload()
        {
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var stream = new MemoryStream();

            await CommandProtocol.WriteCommandAsync(stream, new ArraySegment<byte>(payload));
            stream.Position = 0;
            ArraySegment<byte> read = await CommandProtocol.ReadCommandAsync(stream);

            Assert.That(read.AsSpan().ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void ReadCommandAsync_OversizedLength_ThrowsIOException()
        {
            // Valid magic but a length just past the cap — must reject before renting anything.
            using MemoryStream stream = FrameWith(CommandProtocol.MAGIC_NUMBERS,
                CommandProtocol.MAX_PAYLOAD_BYTES + 1, Array.Empty<byte>());

            Assert.ThrowsAsync<IOException>(async () => await CommandProtocol.ReadCommandAsync(stream));
        }

        [Test]
        public void ReadCommandAsync_NegativeLength_ThrowsIOException()
        {
            using MemoryStream stream = FrameWith(CommandProtocol.MAGIC_NUMBERS, -1, Array.Empty<byte>());

            Assert.ThrowsAsync<IOException>(async () => await CommandProtocol.ReadCommandAsync(stream));
        }

        [Test]
        public void ReadCommandAsync_BadMagic_ThrowsIOException()
        {
            using MemoryStream stream = FrameWith(CommandProtocol.MAGIC_NUMBERS + 1, 0, Array.Empty<byte>());

            Assert.ThrowsAsync<IOException>(async () => await CommandProtocol.ReadCommandAsync(stream));
        }
    }
}
