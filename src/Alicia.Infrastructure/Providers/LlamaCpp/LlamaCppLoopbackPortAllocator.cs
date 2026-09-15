using System.Net;
using System.Net.Sockets;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppLoopbackPortAllocator
{
    public static int Allocate()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);

        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
