using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshake.Serverbound;
using Obsidian.Net.Packets.Status.Clientbound;
using Obsidian.Services;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian;
public partial class Client
{
    private SocketAsyncEventArgs receiveEvent;
    private SocketAsyncEventArgs sendEvent;

    private long sendBufferFlushOffset;

    private bool receiving;
    private bool sending;

    private NetworkBuffer receiveBuffer;
    private NetworkBuffer sendBufferMain;
    private NetworkBuffer sendBufferFlush;

    private readonly Lock sendLock = new();

    public Socket Socket { get; private set; }


    internal void Connect(Socket socket)
    {
        this.Socket = socket;

        this.receiveBuffer = new();
        this.sendBufferMain = new();
        this.sendBufferFlush = new();

        this.receiveEvent = new();
        this.receiveEvent.Completed += OnAsyncCompleted;

        this.sendEvent = new();
        this.sendEvent.Completed += OnAsyncCompleted;

        this.Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

        this.receiveBuffer.Reserve(MaxBufferSize);
        this.sendBufferMain.Reserve(MaxBufferSize);
        this.sendBufferFlush.Reserve(MaxBufferSize);

        this.Connected = true;
    }

    private int Send(byte[] buffer, int offset, int count) => Send(buffer.AsSpan(offset, count));

    private int Send(ReadOnlySpan<byte> buffer)
    {
        if (!this.Connected)
            return 0;

        if (buffer.IsEmpty)
            return 0;

        // Sent data to the client
        var sent = this.Socket.Send(buffer, SocketFlags.None, out SocketError ec);
        if (sent > 0)
            serverMetrics.AddBytesSent(sent);

        // Check for socket error
        if (ec != SocketError.Success)
            Disconnect();

        return sent;
    }

    private bool SendAsync(byte[] buffer) => SendAsync(buffer.AsSpan());


    private bool SendAsync(byte[] buffer, int offset, int size) => SendAsync(buffer.AsSpan(offset, size));

    private bool SendAsync(IClientboundPacket packet)
    {
        if (!this.Connected)
            return false;

        lock (this.sendLock)
        {
            packet.Serialize(this.sendBufferMain);

            if (this.sending)
                return true;
            else
                this.sending = true;

            TrySend();
        }

        return true;
    }

    private bool SendAsync(ReadOnlySpan<byte> buffer)
    {
        if (!this.Connected)
            return false;

        if (buffer.IsEmpty)
            return true;

        lock (this.sendLock)
        {
            this.sendBufferMain.Write(buffer);

            if (this.sending)
                return true;
            else
                this.sending = true;

            TrySend();
        }

        return true;
    }

    #region Processing 
    private async ValueTask TryReceive()
    {
        if (!this.Connected)
            return;

        var process = true;

        while (process)
        {
            process = true;

            try
            {
                this.receiving = true;
                this.receiveEvent.SetBuffer(this.receiveBuffer.Data, 0, (int)this.receiveBuffer.Capacity);

                if (!this.Socket.ReceiveAsync(this.receiveEvent))
                    process = await this.ProcessReceiveAsync(this.receiveEvent);
            }
            catch (ObjectDisposedException) { }
        }
    }
    private void TrySend()
    {
        if (!this.Connected)
            return;

        var empty = false;
        var process = true;

        while (process)
        {
            process = false;

            lock (this.sendLock)
            {
                if (this.sendBufferFlush.IsEmpty)
                {
                    this.sendBufferFlush = Interlocked.Exchange(ref this.sendBufferMain, this.sendBufferFlush);
                    this.sendBufferFlushOffset = 0;

                    if (this.sendBufferFlush.IsEmpty)
                    {
                        empty = true;
                        this.sending = false;
                    }
                }
                else
                    return;
            }

            if (empty)
                return;

            try
            {
                this.sendEvent.SetBuffer(this.sendBufferFlush.Data, (int)this.sendBufferFlushOffset,
                    (int)(this.sendBufferFlush.Size - this.sendBufferFlushOffset));

                if (!this.Socket.SendAsync(this.sendEvent))
                    process = this.ProcessSend(this.sendEvent);
            }
            catch (ObjectDisposedException) { }
        }
    }

    private async ValueTask ProcessPacketAsync()
    {
        if (!this.Connected)
            return;

        var packetData = GetNextPacket();

        if (State == ClientState.Play && packetData.NetworkBuffer.Size < 0)//Empty packets get sent.
            Disconnect();

        switch (State)
        {
            case ClientState.Status: // Server ping/list
                if (packetData.Id == 0x00)
                {
                    var status = new ServerStatus(this.server, this.loggerFactory);

                    await this.eventDispatcher.ExecuteEventAsync(new ServerStatusRequestEventArgs(this.server, status));

                    SendPacket(new StatusResponsePacket(status));
                }
                else if (packetData.Id == 0x01)
                {
                    var pong = Net.Packets.Status.Serverbound.PingRequestPacket.Deserialize(packetData.NetworkBuffer.Data);

                    SendPacket(new PongResponsePacket { Timestamp = pong.Timestamp });
                    Disconnect();
                }
                break;

            case ClientState.Handshaking:
                if (packetData.Id == 0x00)
                {
                    await IntentionPacket.Deserialize(packetData.NetworkBuffer.Data).HandleAsync(this);
                }
                else
                {
                    // Handle legacy ping
                }
                break;

            case ClientState.Login:
                await this.HandlePacketAsync(packetData);
                break;
            case ClientState.Configuration:
                Debug.Assert(Player is not null);

                var result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.server, packetData.Id, packetData.NetworkBuffer.Data));

                if (result == EventResult.Cancelled)
                {
                    this.Logger.LogDebug("configuration packet({id}) {name} was cancelled and is not being processed.",
                        packetData.Id, PacketsRegistry.Configuration.ServerboundNames[packetData.Id]);
                    return;
                }

                await this.HandlePacketAsync(packetData);
                break;
            case ClientState.Play:
                Debug.Assert(Player is not null);

                result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.server, packetData.Id, packetData.NetworkBuffer.Data));

                if (result == EventResult.Cancelled)
                {
                    this.Logger.LogDebug("play packet({id}) {name} was cancelled and is not being processed.",
                        packetData.Id, PacketsRegistry.Play.ServerboundNames[packetData.Id]);
                    return;
                }

                await this.HandlePacketAsync(packetData);

                break;
            case ClientState.Closed:
            default:
                break;
        }
    }

    private async ValueTask<bool> ProcessReceiveAsync(SocketAsyncEventArgs e)
    {
        if (!this.Connected)
            return false;

        var size = e.BytesTransferred;

        if (size > 0)
        {
            this.serverMetrics.AddBytesReceived(size);

            await this.ProcessPacketAsync();

            if (this.receiveBuffer.Capacity == size)
            {
                if (((2 * size) > MaxBufferSize) && (MaxBufferSize > 0))
                {
                    this.Disconnect();
                    return false;
                }

                this.receiveBuffer.Reserve(2 * size);
            }
        }

        this.receiving = false;

        if (e.SocketError == SocketError.Success)
        {
            if (size > 0)
                return true;

            this.Disconnect();
        }
        else
        {
            this.Logger.LogError("An error has occurred");
        }

        return false;
    }

    private bool ProcessSend(SocketAsyncEventArgs e)
    {
        if (!this.Connected) return false;

        var size = e.BytesTransferred;

        if (size > 0)
        {
            this.serverMetrics.AddBytesSent(size);

            this.sendBufferFlushOffset += size;

            if (this.sendBufferFlushOffset == this.sendBufferFlush.Size)
            {
                this.sendBufferFlush.Clear();
                this.sendBufferFlushOffset = 0;
            }
        }

        if (e.SocketError == SocketError.Success)
            return true;

        this.Disconnect();

        return false;
    }
    private async void OnAsyncCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (this.disposed)
            return;

        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                if (await this.ProcessReceiveAsync(e))
                    await this.TryReceive();

                break;
            case SocketAsyncOperation.Send:
                if (this.ProcessSend(e))
                    this.TrySend();

                break;
            default:
                throw new InvalidOperationException("The last operation completed on the socket was not a receive or send");
        }
    }


    #endregion
}
