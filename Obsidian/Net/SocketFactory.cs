using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading;

namespace Obsidian.Net;

internal static class SocketFactory
{
    public static async Task<IConnectionListener> CreateListenerAsync(IPEndPoint endPoint, SocketTransportOptions? options = null, 
        ILoggerFactory? loggerFactory = null, CancellationToken token = default)
    {
        options ??= new SocketTransportOptions();
        loggerFactory ??= NullLoggerFactory.Instance;
        
        var factory = new SocketTransportFactory(Options.Create(options), loggerFactory);
        return await factory.BindAsync(endPoint, token);
    }
}
