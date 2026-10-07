using Microsoft.Extensions.DependencyInjection;
using Obsidian.Utilities.Collections;
using System.Net;
using System.Net.Sockets;

namespace Obsidian;
public partial class Server
{
    // The listening sockets with their acceptors: the configured one, and Open to LAN's in integrated mode. Locked on itself.
    private readonly List<(Socket Socket, SocketAsyncEventArgs Acceptor)> listeners = [];

    internal int bytesPending;
    internal int bytesReceived;
    internal int bytesSent;

    private SimpleObjectPool<SocketAsyncEventArgs> socketEventArgsPool;

    public ConcurrentDictionary<int, IClient> Connections { get; private set; }

    public bool Disposed { get; private set; }

    public required int MaxConnections { get; init; }

    public required int MaxBufferSize { get; init; }

    public bool Started { get; private set; }

    /// <summary>
    /// Starts accepting connections on <see cref="ServerConfiguration.BindAddress"/> and <paramref name="port"/>.
    /// <see cref="Port"/> then holds the bound port, which the operating system picks when <paramref name="port"/> is 0.
    /// </summary>
    public async ValueTask StartAsync(int port)
    {
        this.socketEventArgsPool = new(this.Configuration.MaxPlayers * 2);

        var address = string.IsNullOrWhiteSpace(this.Configuration.BindAddress) ? IPAddress.Any
            : IPAddress.TryParse(this.Configuration.BindAddress, out var parsed) ? parsed
            : throw new FormatException($"BindAddress '{this.Configuration.BindAddress}' isn't an IP address.");

        this.Port = await this.ListenAsync(new IPEndPoint(address, port));

        this.Started = true;
    }

    /// <summary>
    /// Accepts connections on another endpoint too, like Open to LAN does.
    /// </summary>
    /// <returns>The bound port.</returns>
    public async ValueTask<int> ListenAsync(IPEndPoint endpoint)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            socket.Bind(endpoint);
            socket.Listen(this.MaxConnections);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var acceptor = new SocketAsyncEventArgs { UserToken = socket };
        acceptor.Completed += OnAsyncCompleted;

        lock (this.listeners)
            this.listeners.Add((socket, acceptor));

        await this.Accept(acceptor);

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private void CloseListeners()
    {
        lock (this.listeners)
        {
            foreach (var (socket, _) in this.listeners)
                socket.Close();
        }
    }

    private async ValueTask Accept(SocketAsyncEventArgs e)
    {
        try
        {
            e.AcceptSocket = null;

            if (!((Socket)e.UserToken!).AcceptAsync(e))
                await this.ProcessAccept(e);
        }
        catch (ObjectDisposedException)
        {
            // Socket closed
        }
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
        else if (e.SocketError == SocketError.OperationAborted)
        {
            // The listener was closed: the server is stopping.
            return;
        }
        else
        {
            Log.AcceptFailed(this.logger, e.SocketError);
        }

        await this.Accept(e);
    }


    private async ValueTask TryProcessClientAsync(Client client)
    {
        if (!client.Connected)
            return;

        var ip = client.Ip;
        if (Configuration.Whitelist && !WhitelistConfiguration.CurrentValue.WhitelistedIps.Contains(ip))
        {
            Log.NotWhitelisted(this.logger, ip);
            await client.DisconnectAsync("Not whitelisted.");
            return;
        }

        if (this.Configuration.Network.ShouldThrottle)
        {
            if (throttler.TryGetValue(ip, out var time) && time <= DateTimeOffset.UtcNow)
                throttler.Remove(ip, out _);
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

        lock (this.listeners)
        {
            foreach (var (_, acceptor) in this.listeners)
            {
                acceptor.Completed -= this.OnAsyncCompleted;
                acceptor.Dispose();
            }
        }

        this.configWatcher?.Dispose();

        this.Disposed = true;
    }
}
