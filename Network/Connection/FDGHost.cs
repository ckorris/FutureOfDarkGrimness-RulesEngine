using FDG.Network.Messages;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FDG.Network.Connection
{
    public interface INetworkHost
    {
        event Action<ConnectionID>? OnNewClientConnected;

        event Action<ConnectionID>? OnClientDisconnected;

        event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

        Task StartAsync();

        Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled);

        Task SendCommandToSingleClientAsync(ConnectionID client, ArraySegment<byte> data, bool isPooled);

        // Forcibly drop a single client (QF2). Used to evict a rejected or un-greeted connection so an
        // internet-exposed port doesn't leave port-scanners lingering on the roster's broadcast stream.
        void DisconnectClient(ConnectionID client);

        // Marks a connection as having passed the join handshake (#266). Until then its inbound
        // frames are capped at CommandProtocol.MAX_PREAUTH_PAYLOAD_BYTES; afterwards the full
        // MAX_PAYLOAD_BYTES applies (army lists with embedded books are legitimately large).
        // Called by the lobby host at the moment it accepts a greeting.
        void MarkClientAuthenticated(ConnectionID client);

        void Stop();

    }

    public class FDGHost : INetworkHost //ICommandDispatcher
    {
        // Public-exposure limits (#266): a host that lists itself in the server browser (#264) will
        // be found by port scanners, so refuse accepts beyond a sane concurrent total and a small
        // per-address allowance. Both are far above anything a legitimate lobby produces.
        public const int DEFAULT_MAX_CONNECTIONS = 32;
        public const int DEFAULT_MAX_CONNECTIONS_PER_IP = 4;

        // Accessed concurrently from the accept loop, every per-client read loop, and broadcast sends,
        // so it's a concurrent collection rather than a plain Dictionary behind manual locks (#037).
        private readonly ConcurrentDictionary<ConnectionID, ClientConnection> _connectedClients
            = new ConcurrentDictionary<ConnectionID, ClientConnection>();

        private readonly int _maxConnections;
        private readonly int _maxConnectionsPerIp;
        private readonly int _port;

        private TcpListener? _listener;
        private CancellationTokenSource? _cancelTokenSource;
        private bool _isRunning;

        /// <param name="maxConnections">Cap on concurrently accepted connections (#266).</param>
        /// <param name="maxConnectionsPerIp">Cap on concurrent connections from one address (#266).</param>
        /// <param name="port">Listen port. Defaults to the protocol's fixed port; overridable so
        /// transport tests can run in parallel without colliding (the user-facing configurable
        /// port remains #189).</param>
        public FDGHost(int maxConnections = DEFAULT_MAX_CONNECTIONS,
            int maxConnectionsPerIp = DEFAULT_MAX_CONNECTIONS_PER_IP,
            int port = CommandProtocol.TEMP_PORT)
        {
            _maxConnections = maxConnections;
            _maxConnectionsPerIp = maxConnectionsPerIp;
            _port = port;
        }

        public event Action<ConnectionID>? OnNewClientConnected;

        public event Action<ConnectionID>? OnClientDisconnected;

        public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

        public async Task StartAsync()
        {
            _cancelTokenSource = new CancellationTokenSource();

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            Debug.WriteLine($"Host started. Listening on port: {_port}.");

            try
            {
                while (_isRunning && _cancelTokenSource.IsCancellationRequested == false)
                {

                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break; //Listener stopped.
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine($"Exception while accepting a client: {exception.Message}");
                        break;
                    }

                    // Refuse accepts beyond the concurrency caps (#266) before allocating anything
                    // per-connection. Closing immediately is the whole response — a refused scanner
                    // gets no roster events, no read loop, no rented buffers.
                    IPAddress? remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                    if (_connectedClients.Count >= _maxConnections ||
                        CountConnectionsFrom(remoteAddress) >= _maxConnectionsPerIp)
                    {
                        Debug.WriteLine($"Refused connection from {remoteAddress}: connection cap reached " +
                            $"({_connectedClients.Count} total, cap {_maxConnections}/{_maxConnectionsPerIp} per IP).");
                        client.Close();
                        continue;
                    }

                    // Keepalive so a peer that dies silently over a WAN (crash / sleep / NAT mapping expiry)
                    // is detected instead of looking alive forever, and NoDelay so a turn-based decision
                    // isn't held up by Nagle + delayed-ACK (QF3).
                    CommandProtocol.ConfigureSocket(client);

                    Guid guid = Guid.NewGuid();
                    ConnectionID connectionID = new ConnectionID(guid);

                    ClientConnection connection = new ClientConnection(client, remoteAddress);
                    //TODO: Is this the best place to create a new player ID?
                    _connectedClients.TryAdd(connectionID, connection);
                    Debug.WriteLine($"Accepted client. Count: {_connectedClients.Count}");

                    _ = HandleClientAsync(connectionID, connection, _cancelTokenSource.Token);

                    OnNewClientConnected?.Invoke(connectionID);
                }
            }
            finally
            {
                _isRunning = false;
                Debug.WriteLine("Host accept loop ended.");
            }
        }


        private async Task HandleClientAsync(ConnectionID connectionID, ClientConnection connection, CancellationToken cancellationToken)
        {
            TcpClient client = connection.Client;
            using (client)
            {
                NetworkStream stream = client.GetStream();

                try
                {
                    // Un-greeted connections only ever legitimately send one small greeting, so they
                    // get the small frame cap; an oversized declared length throws out of the read
                    // and the finally below drops the connection (#266). The cap is a provider —
                    // evaluated per frame as its length arrives — because MarkClientAuthenticated
                    // fires while the next read is already parked (see ReadCommandAsync's doc).
                    Func<int> maxPayloadBytes = () => connection.IsAuthenticated
                        ? CommandProtocol.MAX_PAYLOAD_BYTES
                        : CommandProtocol.MAX_PREAUTH_PAYLOAD_BYTES;

                    while (cancellationToken.IsCancellationRequested == false)
                    {
                        ArraySegment<byte> payloadSegment = await CommandProtocol.ReadCommandAsync(stream, maxPayloadBytes, cancellationToken)
                            .ConfigureAwait(false);

                        Debug.WriteLine("Received data as host.");

                        OnMessageReceived?.Invoke(payloadSegment, connectionID);

                        if (payloadSegment.Array != null)
                        {
                            ArrayPool<byte>.Shared.Return(payloadSegment.Array);
                        }
                    }
                }
                catch (IOException ioException)
                {
                    Console.WriteLine("Client disconnected or read error: " + ioException.Message);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"Exception in {nameof(HandleClientAsync)}: {exception.Message}");
                }
                finally
                {
                    Debug.WriteLine("Removing client.");
                    _connectedClients.TryRemove(connectionID, out _);
                    client.Close();

                    OnClientDisconnected?.Invoke(connectionID);
                }
            }
        }

        public async Task SendCommandToSingleClientAsync(ConnectionID clientID, ArraySegment<byte> messageBytes, bool isPooled)
        {
            if (_isRunning == false)
            {
                return;
            }

            try
            {
                if (_connectedClients.TryGetValue(clientID, out ClientConnection? connection))
                {
                    await WriteLockedAsync(connection, messageBytes)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Exception while sending command to single client: {exception.Message}");
            }
            finally
            {
                if (messageBytes.Array != null && isPooled)
                {
                    ArrayPool<byte>.Shared.Return(messageBytes.Array);
                }
            }
        }

        public async Task SendCommandToAllAsync(ArraySegment<Byte> messageBytes, bool isPooled)
        {
            // ConcurrentDictionary.Values is a moment-in-time snapshot, so iterating it can't throw
            // even if a client connects or disconnects mid-broadcast (#037).
            List<ClientConnection> clientsCopy = new List<ClientConnection>(_connectedClients.Values);

            foreach (ClientConnection connection in clientsCopy)
            {
                try
                {
                    await WriteLockedAsync(connection, messageBytes)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Exception while broadcasting to all clients: {exception.Message}");
                }
            }

            if (messageBytes.Array != null && isPooled)
            {
                ArrayPool<byte>.Shared.Return(messageBytes.Array);
            }
        }

        // Serializes all writes to a single connection's stream behind its per-connection
        // write lock so the three WriteCommandAsync writes (magic / length / payload) of one
        // frame can't interleave with another sender's frame and corrupt the stream (#086).
        private static async Task WriteLockedAsync(ClientConnection connection, ArraySegment<byte> messageBytes)
        {
            await connection.WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                NetworkStream stream = connection.Client.GetStream();
                await CommandProtocol.WriteCommandAsync(stream, messageBytes)
                    .ConfigureAwait(false);
            }
            finally
            {
                connection.WriteLock.Release();
            }
        }


        // Concurrent connections currently held from one address, counted from the live roster (no
        // separate bookkeeping to drift). Null addresses (exotic transports) never match, so they
        // are only bounded by the total cap.
        private int CountConnectionsFrom(IPAddress? remoteAddress)
        {
            if (remoteAddress == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ClientConnection connection in _connectedClients.Values)
            {
                if (remoteAddress.Equals(connection.RemoteAddress))
                {
                    count++;
                }
            }
            return count;
        }

        public void MarkClientAuthenticated(ConnectionID connectionID)
        {
            if (_connectedClients.TryGetValue(connectionID, out ClientConnection? connection))
            {
                connection.IsAuthenticated = true;
            }
        }

        public void DisconnectClient(ConnectionID connectionID)
        {
            // Just close the socket; the client's read loop (HandleClientAsync) throws, hits its finally,
            // and does the roster removal + OnClientDisconnected exactly as it does for a natural drop.
            if (_connectedClients.TryGetValue(connectionID, out ClientConnection? connection))
            {
                try
                {
                    connection.Client.Close();
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Exception while disconnecting client: {exception.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;

            if (_listener != null)
            {
                _listener.Stop();
            }

            if (_cancelTokenSource != null)
            {
                _cancelTokenSource.Cancel();
            }

            foreach (ClientConnection connection in _connectedClients.Values)
            {
                connection.Client.Close();
            }
            _connectedClients.Clear();

            Debug.WriteLine("Host stopped.");
        }

        // Pairs a connected client's TcpClient with a write lock that serializes outbound
        // frames on its stream (#086), plus the per-connection state the #266 limits need:
        // the remote address (per-IP cap) and whether the join handshake has passed (frame cap).
        private sealed class ClientConnection
        {
            public readonly TcpClient Client;
            public readonly IPAddress? RemoteAddress;
            public readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

            // Written by MarkClientAuthenticated (lobby/engine thread), read by the connection's
            // read loop; volatile so the lifted frame cap is seen promptly.
            public volatile bool IsAuthenticated;

            public ClientConnection(TcpClient client, IPAddress? remoteAddress)
            {
                Client = client;
                RemoteAddress = remoteAddress;
            }
        }
    }
}
