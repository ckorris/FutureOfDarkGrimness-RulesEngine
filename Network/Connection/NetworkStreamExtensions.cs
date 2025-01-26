using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Connection
{
    //TODO: This seems defined in C# already, do I need this?
    internal static class NetworkStreamExtensions
    {
        internal static async Task ReadExactlyAsync(this NetworkStream stream, byte[] buffer,
            int offset, int count, CancellationToken cancellationToken = default)
        {
            int totalRead = 0;
            while(totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalRead, 
                    count - totalRead, cancellationToken)
                    .ConfigureAwait(false);

                if(bytesRead == 0)
                {
                    //The other side closed the connection.
                    throw new IOException("Client side closed the connection.");
                }

                totalRead += bytesRead;
            }
        }
    }
}
