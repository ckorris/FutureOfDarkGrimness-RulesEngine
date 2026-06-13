using FDG.Network.Messages;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FDG.Network.Connection
{
    public interface INetworkClient
    {
        Task<bool> ConnectAsync(IPAddress serverIP);

        event Action<ArraySegment<byte>>? OnMessageReceived;

        Task SendCommandToHost(ArraySegment<byte> command, bool isPooled);

        void Disconnect();
    }

    public class FDGClient : INetworkClient
    {
        private TcpClient? _tcpClient;
        private CancellationTokenSource? _cancelTokenSource;
        private bool _isConnected;

        // Serializes outbound frames so the three WriteCommandAsync writes of one frame can't
        // interleave with a concurrent send and corrupt the stream (#086).
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public event Action<ArraySegment<byte>>? OnMessageReceived;

        public async Task<bool> ConnectAsync(IPAddress serverIP)
        {
            try
            {
                _cancelTokenSource = new CancellationTokenSource();
                _tcpClient = new TcpClient();

                await _tcpClient.ConnectAsync(serverIP, CommandProtocol.TEMP_PORT)
                    .ConfigureAwait(false);

                _isConnected = true;
                Debug.WriteLine("Connected to host.");

                _ = ReceiveLoopAsync(_cancelTokenSource.Token);

                return true;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Exception while trying to connect to host: {exception.Message}");
                return false;
            }
        }

        public async Task SendCommandToHost(ArraySegment<byte> commandBytes, bool isPooled)
        {
            if (_isConnected == false || _tcpClient == null)
            {
                Debug.WriteLine("Cannot send command. Not connected.");
                return;
            }

            try
            {
                NetworkStream stream = _tcpClient.GetStream();
                await _writeLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await CommandProtocol.WriteCommandAsync(stream, commandBytes)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Exception while sending data: {exception.Message}");
                Disconnect();
            }
            finally
            {
                if (commandBytes.Array != null && isPooled)
                {
                    ArrayPool<byte>.Shared.Return(commandBytes.Array);
                }
            }
        }

        public void Disconnect()
        {
            if (_isConnected == false)
            {
                return;
            }

            _isConnected = false;
            if (_tcpClient != null)
            {
                _tcpClient.Close();
            }

            if (_cancelTokenSource != null)
            {
                _cancelTokenSource.Cancel();
            }

            Debug.WriteLine("Client disconnected.");
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (_tcpClient)
                {
                    NetworkStream stream = _tcpClient.GetStream();
                    ConnectionID hostConnectionID = new ConnectionID(Guid.NewGuid());

                    while (cancellationToken.IsCancellationRequested == false)
                    {
                        ArraySegment<byte> payloadSegment = await CommandProtocol.ReadCommandAsync(stream, cancellationToken)
                            .ConfigureAwait(false);

                        Debug.WriteLine("Received data as client.");

                        OnMessageReceived?.Invoke(payloadSegment);
                    }
                }
            }
            catch (IOException ioException)
            {
                Console.WriteLine($"Connection closed, or read error: {ioException.Message}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Exception in {nameof(ReceiveLoopAsync)}: {exception}");
            }
            finally
            {
                Disconnect();
            }
        }
    }
}
