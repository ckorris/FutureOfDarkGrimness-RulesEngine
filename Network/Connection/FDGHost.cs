using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FDG.Network.Connection
{
    public class FDGHost : ICommandDispatcher
    {
        private readonly List<TcpClient> _connectedClients = new List<TcpClient>();
        //private readonly ConcurrentBag<TcpClient> _connectedClients = new ConcurrentBag<TcpClient>();

        private TcpListener _listener;
        private CancellationTokenSource _cancelTokenSource;
        private bool _isRunning;

        public event Action<ArraySegment<byte>> OnCommandReceived;



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

                    lock (_connectedClients) //TODO: Change to concurrent collection.
                    {
                        _connectedClients.Add(client);
                    }
                    Debug.WriteLine("Accepted client.");

                    _ = HandleClientAsync(client, _cancelTokenSource.Token); // '_ =' suppresses warning, but we want to forget it. 
                }
            }
            finally
            {
                _isRunning = false;
                Debug.WriteLine("Host accept loop ended.");
            }
        }


        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using(client)
            {
                NetworkStream stream = client.GetStream();

                try
                {
                    while(cancellationToken.IsCancellationRequested == false)
                    {
                        ArraySegment<byte> payloadSegment = await CommandProtocol.ReadCommandAsync(stream, cancellationToken)
                            .ConfigureAwait(false);

                        OnCommandReceived?.Invoke(payloadSegment);
                    }
                }
                catch(IOException ioException)
                {
                    Console.WriteLine("Client disconnected or read error: " + ioException.Message);
                }
                catch(Exception exception)
                {
                    Console.WriteLine($"Exception in {nameof(HandleClientAsync)}: {exception.Message}");
                }
                finally
                {
                    lock (_connectedClients) //TODO: Change to concurrent collection.
                    {
                        _connectedClients.Remove(client);
                    }
                    client.Close();
                }
            }
        }

        //TODO: Private? Not sure what will need this.
        public async Task SendCommandToSingleClientAsync(TcpClient client, ArraySegment<byte> data)
        {
            if(_isRunning == false || client == null)
            {
                return;
            }

            try
            {
                NetworkStream stream = client.GetStream();
                await CommandProtocol.WriteCommandAsync(stream, data)
                    .ConfigureAwait(false);
            }
            catch(Exception exception)
            {
                Debug.WriteLine($"Exception while sending command to single client: {exception.Message}");
            }
        }

        public async Task SendCommandAsync(ArraySegment<byte> data)
        {
            if(_isRunning == false)
            {
                return;
            }

            List<TcpClient> clientsCopy;

            lock(_connectedClients)
            {
                clientsCopy = new List<TcpClient>();
            }

            foreach(TcpClient client in clientsCopy)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    await CommandProtocol.WriteCommandAsync(stream, data)
                        .ConfigureAwait(false);
                }
                catch(Exception exception)
                {
                    Debug.WriteLine($"Exception while broadcasting to all clients: {exception.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;

            if(_listener != null)
            {
                _listener.Stop();
            }

            if(_cancelTokenSource != null)
            {
                _cancelTokenSource.Cancel();
            }

            lock(_connectedClients)
            {
                foreach(TcpClient client in  _connectedClients)
                {
                    client.Close();
                }
                _connectedClients.Clear();
            }

            Debug.WriteLine("Host stopped.");
        }
    }
}
