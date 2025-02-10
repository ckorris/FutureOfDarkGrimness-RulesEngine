using FDG.Network.Messages;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FDG.Network.Connection
{
    public class FDGClient : ICommandDispatcher
    {
        private TcpClient? _tcpClient;
        private CancellationTokenSource? _cancelTokenSource;
        private bool _isConnected;

        private IMessageSerializer _messageSerializer;

        public FDGClient()
        {
            _messageSerializer = new MessageSerializer();
        }

        internal FDGClient(IMessageSerializer messageSerializer)
        {
            _messageSerializer = messageSerializer;
        }

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
                Debug.WriteLine($"Exception while trying to connect to host: {exception.Message}");
                return false;
            }
        }


        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived)
        {
            _messageSerializer.RegisterForMessageEvent(onMessageReceived);
        }

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe)
        {
            _messageSerializer.DeregisterForMessageEvent(messageToUnsubscribe);
        }

        public async Task SendCommandAsync<TMessage>(TMessage message)
        {
            ArraySegment<byte> commandBytes = _messageSerializer.SerializeMessage(message);

            if (_isConnected == false || _tcpClient == null)
            {
                Debug.WriteLine("Cannot send command. Not connected.");
                return;
            }

            try
            {
                NetworkStream stream = _tcpClient.GetStream();
                await CommandProtocol.WriteCommandAsync(stream, commandBytes)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Exception while sending data: {exception.Message}");
                Disconnect();
            }
            finally
            {
                if (commandBytes.Array != null)
                {
                    ArrayPool<byte>.Shared.Return(commandBytes.Array);
                }
            }
        }

        public Task SendCommandAsync<TMessage>(TMessage message, ConnectionID connectionID)
        {
            //Not expecting this to be called on the client, but can't expect it to know the difference and catch that.
            return SendCommandAsync(message); 
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

                        _messageSerializer.DeserializeMessageAndInvoke(payloadSegment, hostConnectionID);
                    }
                }
            }
            catch (IOException ioException)
            {
                Debug.WriteLine($"Connection closed, or read error: {ioException.Message}");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Exception in {nameof(ReceiveLoopAsync)}: {exception.Message}");
            }
            finally
            {
                Disconnect();
            }
        }
    }
}
