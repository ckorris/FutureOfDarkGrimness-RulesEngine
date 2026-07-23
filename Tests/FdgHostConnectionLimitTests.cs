using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FDG.Network;
using FDG.Network.Connection;
using NUnit.Framework;

namespace FDG.Tests
{
    // #266 — FDGHost pre-authentication limits. A publicly listed host (#264) gets found by port
    // scanners, so: (a) concurrent connections are capped, in total and per source address, and
    // (b) a connection that hasn't passed the join handshake may only send small frames — the full
    // 16 MB allowance is lifted by MarkClientAuthenticated at greeting acceptance.
    //
    // These are real-TCP loopback tests (the first against FDGHost — #065 tracks the wider gap).
    // Each test gets its own port so parallel/repeated runs don't collide, well away from the
    // game's live port 6389.
    [TestFixture]
    [NonParallelizable]
    public class FdgHostConnectionLimitTests
    {
        private const int PortBase = 26389;
        private static int _nextPortOffset;

        private FDGHost? _host;

        [TearDown]
        public void TearDown()
        {
            _host?.Stop();
            _host = null;
        }

        private async Task<int> StartHostAsync(int maxConnections, int maxPerIp,
            Action<FDGHost>? subscribe = null)
        {
            int port = PortBase + Interlocked.Increment(ref _nextPortOffset);
            _host = new FDGHost(maxConnections, maxPerIp, port);
            subscribe?.Invoke(_host);
            _ = _host.StartAsync();

            // Wait until the listener actually accepts before letting the test connect.
            using var probe = new TcpClient();
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await probe.ConnectAsync(IPAddress.Loopback, port);
                    break;
                }
                catch (SocketException) when (attempt < 50)
                {
                    await Task.Delay(20);
                }
            }
            // The probe connection occupies a slot. Wait until the host has OBSERVED it before
            // draining — otherwise WaitForConnectionCountAsync(0) passes trivially (0-0) while the
            // probe's accept event is still in flight, and a handler attached "after the probe"
            // captures the probe after all (this exact race shipped in this file's first draft).
            await WaitForConnectionCountAsync(1);
            probe.Close();
            await WaitForConnectionCountAsync(0);
            return port;
        }

        private async Task WaitForConnectionCountAsync(int expected)
        {
            // OnClientDisconnected lags the socket close slightly; poll briefly instead of racing it.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (CurrentConnectionCount() == expected) return;
                await Task.Delay(20);
            }
            Assert.Fail($"Host connection count never reached {expected}.");
        }

        private int _connected;
        private int _disconnected;
        private int CurrentConnectionCount() => Volatile.Read(ref _connected) - Volatile.Read(ref _disconnected);

        private void TrackRoster(FDGHost host)
        {
            host.OnNewClientConnected += _ => Interlocked.Increment(ref _connected);
            host.OnClientDisconnected += _ => Interlocked.Increment(ref _disconnected);
        }

        /// <summary>True when the peer closed the connection: a read yields EOF or a reset.</summary>
        private static async Task<bool> ClosedByPeerAsync(TcpClient client, int timeoutMs = 5000)
        {
            try
            {
                byte[] buffer = new byte[1];
                using var timeout = new CancellationTokenSource(timeoutMs);
                int read = await client.GetStream().ReadAsync(buffer.AsMemory(), timeout.Token);
                return read == 0;
            }
            catch (OperationCanceledException) { return false; } // still open, nothing arrived
            catch (IOException) { return true; }
            catch (SocketException) { return true; }
        }

        [Test]
        public async Task PerIpCap_ExtraConnectionFromSameAddress_IsRefused()
        {
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 2, TrackRoster);

            using var first = new TcpClient();
            using var second = new TcpClient();
            await first.ConnectAsync(IPAddress.Loopback, port);
            await second.ConnectAsync(IPAddress.Loopback, port);
            await WaitForConnectionCountAsync(2);

            using var third = new TcpClient();
            await third.ConnectAsync(IPAddress.Loopback, port); // TCP-accepts via backlog...

            Assert.That(await ClosedByPeerAsync(third), Is.True,
                "The third connection from one address should be closed by the host (per-IP cap 2).");
            Assert.That(CurrentConnectionCount(), Is.EqualTo(2),
                "A refused connection must never enter the roster.");
        }

        [Test]
        public async Task TotalCap_ExtraConnection_IsRefused()
        {
            int port = await StartHostAsync(maxConnections: 1, maxPerIp: 8, TrackRoster);

            using var first = new TcpClient();
            await first.ConnectAsync(IPAddress.Loopback, port);
            await WaitForConnectionCountAsync(1);

            using var second = new TcpClient();
            await second.ConnectAsync(IPAddress.Loopback, port);

            Assert.That(await ClosedByPeerAsync(second), Is.True,
                "The second connection should be closed by the host (total cap 1).");
        }

        [Test]
        public async Task PreAuth_OversizedDeclaredLength_DropsConnection()
        {
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 8, TrackRoster);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await WaitForConnectionCountAsync(1);

            // A frame header declaring a payload over the pre-auth cap but under the full cap:
            // legal for an authenticated peer, hostile from a stranger.
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(BitConverter.GetBytes(CommandProtocol.MAGIC_NUMBERS));
            await stream.WriteAsync(BitConverter.GetBytes(CommandProtocol.MAX_PREAUTH_PAYLOAD_BYTES + 1));
            await stream.FlushAsync();

            Assert.That(await ClosedByPeerAsync(client), Is.True,
                "An un-greeted connection declaring an oversized frame must be dropped.");
            await WaitForConnectionCountAsync(0);
        }

        [Test]
        public async Task PreAuth_SmallFrame_IsStillReceived()
        {
            var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 8, host =>
            {
                TrackRoster(host);
                host.OnMessageReceived += (payload, _) => received.TrySetResult(payload.Count);
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            // A greeting-sized frame passes the pre-auth cap untouched.
            byte[] payload = new byte[1024];
            await CommandProtocol.WriteCommandAsync(client.GetStream(), new ArraySegment<byte>(payload));

            Task winner = await Task.WhenAny(received.Task, Task.Delay(5000));
            Assert.That(winner, Is.SameAs(received.Task), "The small pre-auth frame never arrived.");
            Assert.That(await received.Task, Is.EqualTo(payload.Length));
        }

        [Test]
        public async Task Authenticated_LargeFrame_IsReceived()
        {
            var connectionIdSource = new TaskCompletionSource<ConnectionID>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 8, host =>
            {
                TrackRoster(host);
                host.OnMessageReceived += (payload, _) => received.TrySetResult(payload.Count);
            });

            // Attach only after StartHostAsync's readiness probe has come and gone, so the captured
            // ConnectionID is the test client's, not the probe's.
            _host!.OnNewClientConnected += id => connectionIdSource.TrySetResult(id);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            Task idWinner = await Task.WhenAny(connectionIdSource.Task, Task.Delay(5000));
            Assert.That(idWinner, Is.SameAs(connectionIdSource.Task), "Host never reported the connection.");
            _host!.MarkClientAuthenticated(await connectionIdSource.Task);

            // Over the pre-auth cap, under the full cap — exactly the shape of a real army-list
            // update. The read loop is already parked on this connection with the pre-auth value,
            // which is why the cap must be evaluated per frame (the provider seam this pins).
            byte[] payload = new byte[CommandProtocol.MAX_PREAUTH_PAYLOAD_BYTES + 1];
            await CommandProtocol.WriteCommandAsync(client.GetStream(), new ArraySegment<byte>(payload));

            Task winner = await Task.WhenAny(received.Task, Task.Delay(5000));
            Assert.That(winner, Is.SameAs(received.Task), "The authenticated large frame never arrived.");
            Assert.That(await received.Task, Is.EqualTo(payload.Length));
        }

        // ---- Broadcast gating (#189) --------------------------------------------------------
        // SendCommandToAllAsync must reach only greeted/accepted (authenticated) connections, so a
        // scanner or still-greeting client never sees roster / chat / replicated game state.

        /// <summary>True if a framed broadcast payload arrives on this client within the window.</summary>
        private static async Task<bool> ReceivesBroadcastAsync(TcpClient client, int timeoutMs = 2000)
        {
            try
            {
                using var timeout = new CancellationTokenSource(timeoutMs);
                ArraySegment<byte> payload = await CommandProtocol.ReadCommandAsync(client.GetStream(), timeout.Token);
                return payload.Count > 0;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception) { return false; }
        }

        [Test]
        public async Task Broadcast_UnauthenticatedConnection_ReceivesNothing()
        {
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 8, TrackRoster);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await WaitForConnectionCountAsync(1); // connected, but never greeted -> not authenticated

            byte[] payload = new byte[64];
            await _host!.SendCommandToAllAsync(new ArraySegment<byte>(payload), isPooled: false);

            Assert.That(await ReceivesBroadcastAsync(client), Is.False,
                "An un-greeted connection must not receive broadcasts (#189).");
        }

        [Test]
        public async Task Broadcast_AuthenticatedConnection_Receives()
        {
            var connectionIdSource = new TaskCompletionSource<ConnectionID>(TaskCreationOptions.RunContinuationsAsynchronously);
            int port = await StartHostAsync(maxConnections: 8, maxPerIp: 8, TrackRoster);
            // Attach after the readiness probe so the captured id is the test client's.
            _host!.OnNewClientConnected += id => connectionIdSource.TrySetResult(id);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            Task idWinner = await Task.WhenAny(connectionIdSource.Task, Task.Delay(5000));
            Assert.That(idWinner, Is.SameAs(connectionIdSource.Task), "Host never reported the connection.");
            _host.MarkClientAuthenticated(await connectionIdSource.Task);

            byte[] payload = new byte[64];
            await _host.SendCommandToAllAsync(new ArraySegment<byte>(payload), isPooled: false);

            Assert.That(await ReceivesBroadcastAsync(client), Is.True,
                "An authenticated roster member must receive broadcasts (#189).");
        }
    }
}
