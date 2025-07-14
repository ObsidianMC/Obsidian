using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net;
using Obsidian.Net.Packets.Handshake.Serverbound;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Net.Packets.Status.Clientbound;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using static Obsidian.API.Registries.CodecRegistry;

namespace Obsidian;
public partial class Client
{
    private SocketAsyncEventArgs receiveEvent;
    private SocketAsyncEventArgs sendEvent;

    private int sendBufferFlushOffset;

    private bool receiving;
    private bool sending;

    private NetworkBuffer receiveBuffer;
    private NetworkBuffer sendBufferMain;
    private NetworkBuffer sendBufferFlush;

    private readonly Lock sendLock = new();

    public Socket Socket { get; private set; }

    private bool loginPending;

    internal async ValueTask ConnectAsync(Socket socket)
    {
        this.Socket = socket;

        this.receiveBuffer = new();
        this.sendBufferMain = new();
        this.sendBufferFlush = new();

        this.receiveEvent = this.pool.Get();
        this.receiveEvent.Completed += OnAsyncCompleted;

        this.sendEvent = this.pool.Get();
        this.sendEvent.Completed += OnAsyncCompleted;

        this.receiveBuffer.Reserve(MaxBufferSize);

        _ = this.HandlePacketQueueAsync();

        await this.TryReceiveAsync();
    }

    private bool SendAsync(IClientboundPacket packet)
    {
        if (!this.Connected)
            return false;

        string name = "";

        if (this.State == ClientState.Login)
            PacketsRegistry.Login.ClientboundNames.TryGetValue(packet.Id, out name);
        else if (this.State == ClientState.Configuration)
            PacketsRegistry.Configuration.ClientboundNames.TryGetValue(packet.Id, out name);
        else if (this.State == ClientState.Play)
            PacketsRegistry.Play.ClientboundNames.TryGetValue(packet.Id, out name);

        this.Logger.LogDebug("Sending packet({name})", name);
        lock (this.sendLock)
        {
            this.sendBufferMain.WritePacket(packet);

            if (this.sending)
                return true;
            else
                this.sending = true;

            TrySend();
        }

        return true;
    }
    #region Processing 
    private async ValueTask TryReceiveAsync()
    {
        if (this.receiving || !this.Connected)
            return;

        var process = true;
        while (process)
        {
            if (this.loginPending)
                continue;

            process = false;
            try
            {
                this.receiving = true;
                this.receiveEvent.SetBuffer(this.receiveBuffer.Data, this.receiveBuffer.Offset, this.receiveBuffer.Capacity);

                var willRaiseEvent = this.Socket.ReceiveAsync(this.receiveEvent);
                if (!willRaiseEvent)
                    process = await this.ProcessReceiveAsync(this.receiveEvent);
            }
            catch (ObjectDisposedException) { }
        }

        if (this.receiving)
            this.receiving = false;
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
                this.sendEvent.SetBuffer(this.sendBufferFlush.Data, this.sendBufferFlushOffset, this.sendBufferFlush.Offset);

                if (!this.Socket.SendAsync(this.sendEvent))
                    process = this.ProcessSend(this.sendEvent);
            }
            catch (ObjectDisposedException) { }
        }
    }

    private PacketData GetNextPacket()
    {
        try
        {
            var length = this.receiveBuffer.ReadVarInt();
            if (length == 0)
                throw new UnreachableException("Packet length returned 0");

            var packetId = this.receiveBuffer.ReadVarInt();

            var varLen = packetId.GetVarIntLength();

            var packetDataLength = Math.Max(length - varLen, 0);

            var packetData = this.receiveBuffer.Read(packetDataLength);

            return new PacketData { Id = packetId, NetworkBuffer = packetData };
        }
        catch { }

        this.receiveBuffer.Clear();
        this.receiveBuffer.Reserve(MaxBufferSize);

        return PacketData.Default;
    }

    private async ValueTask ProcessPacketAsync(int bytesReceived)
    {
        if (!this.Connected)
            return;

        while (this.receiveBuffer.Offset < bytesReceived)
        {
            var packetData = GetNextPacket();

            switch (State)
            {
                case ClientState.Status: // Server ping/list
                    if (packetData.Id == 0x00)
                    {
                        var status = new ServerStatus(this.Server, ServerConstants.ServerIcon);

                        await this.eventDispatcher.ExecuteEventAsync(new ServerStatusRequestEventArgs(this.Server, status));

                        SendPacket(new StatusResponsePacket(status));
                    }
                    else if (packetData.Id == 0x01)
                    {
                        var pong = Net.Packets.Status.Serverbound.PingRequestPacket.Deserialize(packetData.NetworkBuffer.Data);

                        SendPacket(new PongResponsePacket { Timestamp = pong.Timestamp });
                    }
                    break;

                case ClientState.Handshaking:
                    if (packetData.Id != 0x00)
                        return;

                    await IntentionPacket.Deserialize(packetData.NetworkBuffer.Data).HandleAsync(this);
                    break;

                case ClientState.Login:
                    await this.HandlePacketAsync(packetData);
                    break;
                case ClientState.Configuration:
                    Debug.Assert(Player is not null);

                    var result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.Server, packetData.Id, packetData.NetworkBuffer.Data));

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

                    result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player, this.Server, packetData.Id, packetData.NetworkBuffer.Data));

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

        if (this.loginPending)
        {
            this.receiveBuffer = new EncryptedNetworkBuffer(sharedKey);
            this.sendBufferMain = new EncryptedNetworkBuffer(sharedKey);
            this.sendBufferFlush = new EncryptedNetworkBuffer(sharedKey);

            this.receiveBuffer.Reserve(MaxBufferSize);

            this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
            {
                SkinProperties = this.Player.SkinProperties,
            });

            this.Logger.LogDebug("Sent Login success to user {Username} {UUID}", this.Player.Username, this.Player.Uuid);

            this.loginPending = false;
        }
    }

    private async ValueTask<bool> ProcessReceiveAsync(SocketAsyncEventArgs e)
    {
        if (!this.Connected)
            return false;

        var size = e.BytesTransferred;

        if (size > 0)
        {
            this.receiveBuffer.BytesPending = size;

            this.serverMetrics.AddBytesReceived(size);

            await this.ProcessPacketAsync(size);

            if (this.receiveBuffer.BytesPending <= 0)
            {
                if (2 * size > MaxBufferSize)
                {
                    this.Disconnect();
                    return false;
                }

                this.receiveBuffer.Clear();
                this.receiveBuffer.Reserve(MaxBufferSize);
            }
        }

        this.receiving = false;

        if (e.SocketError == SocketError.Success)
        {
            if (size > 0)
                return true;
            else
                this.Disconnect();
        }
        else
        {
            this.Logger.LogError("An error has occurred: {error}", e.SocketError);
            this.Disconnect();
        }

        return false;
    }

    private bool ProcessSend(SocketAsyncEventArgs e)
    {
        if (!this.Connected)
            return false;

        var size = e.BytesTransferred;

        if (size > 0)
        {
            this.serverMetrics.AddBytesSent(size);

            this.sendBufferFlushOffset += size;

            if (this.sendBufferFlushOffset == this.sendBufferFlush.Offset)
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
        if (!this.Connected || this.disposed)
            return;

        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                if (await this.ProcessReceiveAsync(e))
                    await this.TryReceiveAsync();

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
