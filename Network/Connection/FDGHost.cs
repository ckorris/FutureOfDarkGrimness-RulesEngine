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

        void Stop();

    }

    public class FDGHost : INetworkHost //ICommandDispatcher
    {
        // Accessed concurrently from the accept loop, every per-client read loop, and broadcast sends,
        // so it's a concurrent collection rather than a plain Dictionary behind manual locks (#037).
        private readonly ConcurrentDictionary<ConnectionID, ClientConnection> _connectedClients
            = new ConcurrentDictionary<ConnectionID, ClientConnection>();

        private TcpListener? _listener;
        private CancellationTokenSource? _cancelTokenSource;
        private bool _isRunning;

        public event Action<ConnectionID>? OnNewClientConnected;

        public event Action<ConnectionID>? OnClientDisconnected;

        public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

        public async Task StartAsync()
        {
            _cancelTokenSource = new CancellationTokenSource();

            _listener = new TcpListener(IPAddress.Any, CommandProtocol.TEMP_PORT);
            _listener.Start();
            _isRunning = true;

            Debug.WriteLine($"Host started. Listening on port: {CommandProtocol.TEMP_PORT}.");

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

                    // Keepalive so a peer that dies silently over a WAN (crash / sleep / NAT mapping expiry)
                    // is detected instead of looking alive forever, and NoDelay so a turn-based decision
                    // isn't held up by Nagle + delayed-ACK (QF3).
                    CommandProtocol.ConfigureSocket(client);

                    Guid guid = Guid.NewGuid();
                    ConnectionID connectionID = new ConnectionID(guid);

                    //TODO: Is this the best place to create a new player ID?
                    _connectedClients.TryAdd(connectionID, new ClientConnection(client));
                    Debug.WriteLine($"Accepted client. Count: {_connectedClients.Count}");

                    _ = HandleClientAsync(connectionID, client, _cancelTokenSource.Token);

                    OnNewClientConnected?.Invoke(connectionID);
                }
            }
            finally
            {
                _isRunning = false;
                Debug.WriteLine("Host accept loop ended.");
            }
        }


        private async Task HandleClientAsync(ConnectionID connectionID, TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                try
                {
                    while (cancellationToken.IsCancellationRequested == false)
                    {
                        ArraySegment<byte> payloadSegment = await CommandProtocol.ReadCommandAsync(stream, cancellationToken)
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
        // frames on its stream (#086).
        private sealed class ClientConnection
        {
            public readonly TcpClient Client;
            public readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

            public ClientConnection(TcpClient client)
            {
                Client = client;
            }
        }
    }
}
