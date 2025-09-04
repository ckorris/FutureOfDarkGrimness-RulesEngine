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

        event Action<ArraySegment<byte>>? OnMessageReceived;

        Task StartAsync();

        Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled);

        Task SendCommandToSingleClientAsync(ConnectionID client, ArraySegment<byte> data, bool isPooled);

        void Stop();

    }

    public class FDGHost : INetworkHost //ICommandDispatcher
    {
        private readonly Dictionary<ConnectionID, TcpClient> _connectedClients
            = new Dictionary<ConnectionID, TcpClient>();

        private TcpListener? _listener;
        private CancellationTokenSource? _cancelTokenSource;
        private bool _isRunning;

        public event Action<ConnectionID>? OnNewClientConnected;

        public event Action<ConnectionID>? OnClientDisconnected;

        public event Action<ArraySegment<byte>>? OnMessageReceived;

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

                        _connectedClients.Add(connectionID, client);
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

                        OnMessageReceived?.Invoke(payloadSegment);

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
                TcpClient client = _connectedClients[clientID];
                NetworkStream stream = client.GetStream();
                await CommandProtocol.WriteCommandAsync(stream, messageBytes)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Exception while sending command to single client: {exception.Message}");
            }
            finally
            {
                if (messageBytes.Array != null)
                {
                    ArrayPool<byte>.Shared.Return(messageBytes.Array);
                }
            }
        }

        public async Task SendCommandToAllAsync(ArraySegment<Byte> messageBytes, bool isPooled)
        {
            List<TcpClient> clientsCopy;

            lock (_connectedClients)
            {
                clientsCopy = new List<TcpClient>(_connectedClients.Values);
            }

            Debug.WriteLine($"Sending command to {clientsCopy.Count} clients.");

            foreach (TcpClient client in clientsCopy)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    await CommandProtocol.WriteCommandAsync(stream, messageBytes)
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
                foreach (TcpClient client in _connectedClients.Values)
                {
                    client.Close();
                }
                _connectedClients.Clear();
            }

            Debug.WriteLine("Host stopped.");
        }


    }
}
