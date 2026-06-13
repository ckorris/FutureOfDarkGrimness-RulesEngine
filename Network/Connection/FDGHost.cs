using FDG.Network.Messages;
using System.Buffers;
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

        void Stop();

    }

    public class FDGHost : INetworkHost //ICommandDispatcher
    {
        private readonly Dictionary<ConnectionID, ClientConnection> _connectedClients
            = new Dictionary<ConnectionID, ClientConnection>();

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

                    Guid guid = Guid.NewGuid();
                    ConnectionID connectionID = new ConnectionID(guid);

                    lock (_connectedClients) //TODO: Change to concurrent collection.
                    {
                        //TODO: Is this the best place to create a new player ID? 

                        _connectedClients.Add(connectionID, new ClientConnection(client));
                        Debug.WriteLine($"Accepted client. Count: {_connectedClients.Count}");
                    }

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
                    lock (_connectedClients) //TODO: Change to concurrent collection.
                    {
                        _connectedClients.Remove(connectionID);
                    }
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
                ClientConnection connection;
                lock (_connectedClients)
                {
                    connection = _connectedClients[clientID];
                }
                await WriteLockedAsync(connection, messageBytes)
                    .ConfigureAwait(false);
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
            List<ClientConnection> clientsCopy;

            lock (_connectedClients)
            {
                clientsCopy = new List<ClientConnection>(_connectedClients.Values);
            }

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

            lock (_connectedClients)
            {
                foreach (ClientConnection connection in _connectedClients.Values)
                {
                    connection.Client.Close();
                }
                _connectedClients.Clear();
            }

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
