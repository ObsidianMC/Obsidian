using Microsoft.Extensions.Logging;
using Obsidian.Services;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian;
public abstract class SocketManager(ILogger<SocketManager> logger, ILoggerFactory loggerFactory, IUserCache userCache) : IDisposable
{
    private readonly ILogger<SocketManager> logger = logger;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly IUserCache userCache = userCache;

    private Socket socket;

    private int totalConnectedSockets;
    internal int bytesPending;
    internal int bytesReceived;
    internal int bytesSent;


    private SocketAsyncEventArgs acceptorEventArgs;

    protected Dictionary<int, Client> Connections { get; private set; }

    public bool Disposed { get; private set; }

    public required int MaxConnections { get; init; }

    public required int MaxBufferSize { get; init; }

    public bool Started { get; private set; }

    public void Start(int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, port);

        this.acceptorEventArgs = new();
        this.acceptorEventArgs.Completed += OnAsyncCompleted;

        this.Connections = new Dictionary<int, Client>(this.MaxConnections);

        this.socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);

        this.socket.Bind(endpoint);

        this.socket.Listen(this.MaxConnections);

        this.Started = true;

        this.Accept(this.acceptorEventArgs);
    }

    protected virtual void OnError(SocketError error) { }

    internal void RegisterClient(Client client) => this.Connections.Add(client.id, client);

    internal void UnregisterClient(int id) => this.Connections.Remove(id);

    private void Accept(SocketAsyncEventArgs e)
    {
        e.AcceptSocket = null;

        if (!this.socket.AcceptAsync(e))
            this.ProcessAccept(e);
    }

    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            var client = new Client(this, this.loggerFactory, this.userCache);

            Interlocked.Increment(ref this.totalConnectedSockets);

            client.Connect(e.AcceptSocket);
        }
        else
            this.SendError(e.SocketError);

        this.Accept(e);
    }

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

        Interlocked.Decrement(ref this.totalConnectedSockets);

        this.logger.LogInformation("Client {address} was disconnected.", e.RemoteEndPoint);
    }

    protected void OnAsyncCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (this.Disposed)
            return;

        this.ProcessAccept(e);
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);

        this.acceptorEventArgs.Completed -= this.OnAsyncCompleted;
        this.acceptorEventArgs.Dispose();

        this.Disposed = true;
    }
}
