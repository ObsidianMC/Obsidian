using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian;
public partial class Server
{
    private Socket socket;

    internal int bytesPending;
    internal int bytesReceived;
    internal int bytesSent;

    private SocketAsyncEventArgs acceptorEventArgs;

    public ConcurrentDictionary<int, Client> Connections { get; private set; }

    public bool Disposed { get; private set; }

    public required int MaxConnections { get; init; }

    public required int MaxBufferSize { get; init; }

    public bool Started { get; private set; }

    public async ValueTask Start(int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, port);

        this.acceptorEventArgs = new();
        this.acceptorEventArgs.Completed += OnAsyncCompleted;

        this.Connections = new ConcurrentDictionary<int, Client>(-1, this.MaxConnections);

        this.socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);

        this.socket.Bind(endpoint);

        this.socket.Listen(this.MaxConnections);

        this.Started = true;

        await this.Accept(this.acceptorEventArgs);
    }

    private void OnError(SocketError error) { }

    internal void RegisterClient(Client client) => this.Connections.TryAdd(client.Id, client);

    internal void UnregisterClient(int id) => this.Connections.Remove(id, out _);

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

            client.Connect(e.AcceptSocket);

            if (!this.WorldManager.ReadyToJoin)
            {
                await client.DisconnectAsync("World not ready to join");
                return;
            }

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
        }
        else
            this.SendError(e.SocketError);

        await this.Accept(e);
    }

    private Client CreateClient() => new Client(this.EventDispatcher, this, this.loggerFactory, this.userCache, this.serverMetrics);

    public void SendError(SocketError error)
    {
        // Skip disconnect errors
        if ((error == SocketError.ConnectionAborted) ||
            (error == SocketError.ConnectionRefused) ||
            (error == SocketError.ConnectionReset) ||
            (error == SocketError.OperationAborted) ||
            (error == SocketError.Shutdown))
            return;

        OnError(error);
    }

    private void CloseSocket(SocketAsyncEventArgs e)
    {
        var socket = (Socket)e.UserToken;

        try
        {
            socket.Shutdown(SocketShutdown.Send);
        }
        catch { }

        socket.Close();

        this._logger.LogInformation("Client {address} was disconnected.", e.RemoteEndPoint);
    }

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
