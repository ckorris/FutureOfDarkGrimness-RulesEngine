using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Connection
{
    public class FDGClient
    {
        private TcpClient _tcpClient;
        private CancellationTokenSource _cancelTokenSource;
        private bool _isConnected;

        public event Action<ArraySegment<byte>> OnCommandReceived;

        public async Task<bool> ConnectAsync(string serverIP, int port)
        {
            try
            {
                _cancelTokenSource = new CancellationTokenSource();
                _tcpClient = new TcpClient();

                await _tcpClient.ConnectAsync(serverIP, port).ConfigureAwait(false);

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

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (_tcpClient)
                {
                    NetworkStream stream = _tcpClient.GetStream();

                    while(cancellationToken.IsCancellationRequested == false)
                    {
                        ArraySegment<byte> payloadSegment = await CommandProtocol.ReadCommandAsync(stream, cancellationToken)
                            .ConfigureAwait(false);

                        OnCommandReceived?.Invoke(payloadSegment);
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

        public async Task SendCommandAsync(ArraySegment<byte> commandBytes)
        {
            if(_isConnected == false || _tcpClient == null)
            {
                Debug.WriteLine("Cannot send command. Not connect.");
                return;
            }

            try
            {
                NetworkStream stream = _tcpClient.GetStream();
                await CommandProtocol.WriteCommandAsync(stream, commandBytes)
                    .ConfigureAwait(false);
            }
            catch(Exception exception)
            {
                Debug.WriteLine($"Exception while sending data: {exception.Message}");
                Disconnect();
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

            if(_cancelTokenSource != null)
            {
                _cancelTokenSource.Cancel();
            }

            Debug.WriteLine("Client disconnected.");
        }
    }
}
