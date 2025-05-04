using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.Utilities.Collections;
using System.Net;
using System.Net.Sockets;

namespace Obsidian;
public partial class Server
{
    private Socket socket;

    internal int bytesPending;
    internal int bytesReceived;
    internal int bytesSent;

    private SocketAsyncEventArgs acceptorEventArgs;
    private SimpleObjectPool<SocketAsyncEventArgs> socketEventArgsPool;

    public ConcurrentDictionary<int, IClient> Connections { get; private set; }

    public bool Disposed { get; private set; }

    public required int MaxConnections { get; init; }

    public required int MaxBufferSize { get; init; }

    public bool Started { get; private set; }

    public async ValueTask StartAsync(int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, port);
        this.socketEventArgsPool = new(this.Configuration.MaxPlayers * 2);

        this.acceptorEventArgs = new();
        this.acceptorEventArgs.Completed += OnAsyncCompleted;

        this.socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);

        this.socket.Bind(endpoint);

        this.socket.Listen(this.MaxConnections);

        this.Started = true;

        await this.Accept(this.acceptorEventArgs);
    }

    private async ValueTask Accept(SocketAsyncEventArgs e)
    {
        e.AcceptSocket = null;

        if (!this.socket.AcceptAsync(e))
            await this.ProcessAccept(e);
    }

    private async ValueTask ProcessAccept(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            var client = this.CreateClient();

            await client.ConnectAsync(e.AcceptSocket);

            if (!this.WorldManager.ReadyToJoin)
            {
                await client.DisconnectAsync("World not ready to join");
                await this.Accept(e);
                return;
            }

            await this.TryProcessClientAsync(client);
        }
        else
            this._logger.LogError("An error has occurred on a socket with the error {error}.", e.SocketError);

        await this.Accept(e);
    }


    private async ValueTask TryProcessClientAsync(IClient client)
    {
        if (!client.Connected)
            return;

        var ip = client.Ip;
        if (Configuration.Whitelist && !WhitelistConfiguration.CurrentValue.WhitelistedIps.Contains(ip))
        {
            _logger.LogInformation("{ip} is not whitelisted. Closing connection", ip);
            await client.DisconnectAsync("Not whitelisted.");
            return;
        }

        if (this.Configuration.Network.ShouldThrottle)
        {
            if (throttler.TryGetValue(ip, out var time) && time <= DateTimeOffset.UtcNow)
            {
                throttler.Remove(ip, out _);
                _logger.LogDebug("Removed {ip} from throttler", ip);
            }
        }

        this.Connections.TryAdd(client.Id, client);
    }

    private Client CreateClient() => ActivatorUtilities.CreateInstance<Client>(this.serviceProvider, this.socketEventArgsPool);

    private async void OnAsyncCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (this.Disposed)
            return;

        await this.ProcessAccept(e);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        this.acceptorEventArgs.Completed -= this.OnAsyncCompleted;
        this.acceptorEventArgs.Dispose();

        this.configWatcher?.Dispose();

        this.Disposed = true;
    }
}
