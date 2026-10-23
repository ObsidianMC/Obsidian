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
    private NetworkBuffer sendBufferMain = new();
    private NetworkBuffer sendBufferFlush = new();

    private readonly Lock sendLock = new();

    // Packets serialized and waiting for a network thread to frame them into sendBufferMain, in order; guarded by
    // sendLock. The flush under way swaps it with its own list, which only it uses.
    private List<OutgoingFrame> pendingFrames = [];
    private List<OutgoingFrame> framingFrames = [];

    // Whether a flush of pendingFrames is posted to the network threads or running; guarded by sendLock. There's one
    // at a time, so frames are framed in order.
    private bool flushScheduled;

    // The flush's: frames are framed and encrypted into it, then appended to sendBufferMain under sendLock.
    private readonly NetworkBuffer framedOutput = new();
    private readonly PacketFramer framer = new();
    private readonly Action flushOutput;

    // Completed only when no frames wait and both socket buffers have emptied, or teardown abandons them.
    private TaskCompletionSource? outputDrained;

    public Socket Socket { get; private set; }

    private bool loginPending;

    internal async ValueTask ConnectAsync(Socket socket)
    {
        this.Socket = socket;

        this.receiveBuffer = new();

        this.receiveEvent = this.pool.Get();
        this.receiveEvent.Completed += OnAsyncCompleted;

        this.sendEvent = this.pool.Get();
        this.sendEvent.Completed += OnAsyncCompleted;

        this.receiveBuffer.Reserve(MaxBufferSize);

        await this.TryReceiveAsync();
    }

    // Serializes the packet here, so it's sent as it is now, and leaves framing (compressing, encrypting) and writing it
    // to the network threads (see FlushOutput). Bodies a broadcast serialized already for connections like this one (see
    // SharedPacketBodies) are framed as they are.
    private bool SendAsync(IClientboundPacket packet, bool terminal = false, IReadOnlyList<NetworkBuffer>? bodies = null)
    {
        if (this.IsClosing || !this.Connected)
            return false;

        if (bodies is null)
        {
            var serialized = new List<NetworkBuffer>(1);
            NetworkBuffer.SerializeBodies(packet, this.ReadsPluginEntityData(packet), serialized);
            bodies = serialized;
        }

        lock (this.sendLock)
        {
            if (this.IsClosing || !this.Connected || (!terminal && !this.Accepts(packet)))
                return false;

            // The threshold it's framed with is the one it was sent under, so the compression announcement isn't
            // compressed and every packet after it is.
            foreach (var body in bodies)
                this.pendingFrames.Add(new OutgoingFrame(body, this.compressionThreshold));

            this.ScheduleFlush();
        }

        return true;
    }

    // Encrypts the output from the packets sent after this, like the login's encryption does once the client answered.
    private void StartEncryptingOutput(byte[] key)
    {
        lock (this.sendLock)
        {
            this.pendingFrames.Add(new OutgoingFrame(Body: null, CompressionThreshold: -1, EncryptionKey: key));
            this.ScheduleFlush();
        }
    }

    // Called under sendLock.
    private void ScheduleFlush()
    {
        if (this.flushScheduled)
            return;

        this.flushScheduled = true;
        WorkerThreads.Network.Post(this.flushOutput);
    }

    // Runs on a network thread: frames what waits, appends it to the socket's buffer and starts sending it. What's sent
    // meanwhile waits for another round, posted behind other connections' output so a busy one doesn't hold a thread.
    private void FlushOutput()
    {
        try
        {
            lock (this.sendLock)
            {
                if (this.IsClosing)
                {
                    this.flushScheduled = false;
                    return;
                }

                (this.pendingFrames, this.framingFrames) = (this.framingFrames, this.pendingFrames);
            }

            foreach (var frame in this.framingFrames)
                this.framer.Write(this.framedOutput, frame);

            this.framingFrames.Clear();

            var startSending = false;
            lock (this.sendLock)
            {
                if (!this.IsClosing && this.framedOutput.Offset > 0)
                {
                    this.sendBufferMain.Write(this.framedOutput.AsSpan(0, this.framedOutput.Offset));

                    startSending = !this.sending;
                    this.sending = true;
                }

                // Cleared before the next round can start on another thread.
                this.framedOutput.Clear();

                if (!this.IsClosing && this.pendingFrames.Count > 0)
                {
                    WorkerThreads.Network.Post(this.flushOutput);
                }
                else
                {
                    this.flushScheduled = false;
                    if (!this.sending)
                        this.outputDrained?.TrySetResult();
                }
            }

            if (startSending)
                this.TrySend();
        }
        catch (Exception exception)
        {
            lock (this.sendLock)
                this.flushScheduled = false;

            Log.OutputFailed(this.Logger, exception);

            // Disconnecting raises Disconnected, whose handlers may fail too; that's logged rather than left to the
            // network thread.
            try
            {
                this.Disconnect();
            }
            catch (Exception disconnectFailure)
            {
                Log.DisconnectFailed(this.Logger, disconnectFailure);
            }
        }
    }

    // Whether the packet may hold entity data and the client reads plugins' data fields in it.
    private bool ReadsPluginEntityData(IClientboundPacket packet) =>
        packet is Net.Packets.Play.Clientbound.SetEntityDataPacket or Net.Packets.Play.Clientbound.BundledPacket
        && this.Channels.HasNeoForgePlayChannel(Net.Payloads.ChannelPayloads.PluginEntityData);

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
                this.receiveEvent.SetBuffer(this.receiveBuffer.GetBuffer(), this.receiveBuffer.Offset, this.receiveBuffer.Capacity);

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
        var process = true;

        while (process)
        {
            bool completedSynchronously;

            // Submitted under the lock, which Disconnect returns the send event to the pool under, so a closing client
            // never sets up an event another connection already took.
            lock (this.sendLock)
            {
                if (this.IsClosing || !this.Connected)
                    return;

                // A client whose socket loops never started (as tests make them) has nowhere to send its output.
                if (this.sendEvent is null)
                    this.sendBufferMain.Clear();

                if (this.sendBufferFlush.IsEmpty)
                {
                    this.sendBufferFlush = Interlocked.Exchange(ref this.sendBufferMain, this.sendBufferFlush);
                    this.sendBufferFlushOffset = 0;

                    if (this.sendBufferFlush.IsEmpty)
                    {
                        this.sending = false;
                        if (!this.flushScheduled)
                            this.outputDrained?.TrySetResult();

                        return;
                    }
                }

                try
                {
                    this.sendEvent.SetBuffer(
                        this.sendBufferFlush.GetBuffer(),
                        this.sendBufferFlushOffset,
                        this.sendBufferFlush.Offset - this.sendBufferFlushOffset);

                    completedSynchronously = !this.Socket.SendAsync(this.sendEvent);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }

            process = completedSynchronously && this.ProcessSend(this.sendEvent);
        }
    }

    private PacketData GetNextPacket()
    {
        try
        {
            var length = this.receiveBuffer.ReadVarInt();
            if (length == 0)
                throw new UnreachableException("Packet length returned 0");

            if (this.compressionThreshold >= 0)
                return NetworkBuffer.ReadCompressedPacket(this.receiveBuffer.Read(length).GetBuffer());

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
                        var status = new ServerStatus(this.HostServer, ServerConstants.ServerIcon)
                        {
                            IsModded = ((Server)this.HostServer).PayloadChannels.HasChannels
                                || ((Server)this.HostServer).GetModdedRegistries().Count > 0
                        };

                        var request = new ServerStatusRequestEventArgs(this.HostServer, status);
                        await this.eventDispatcher.ExecuteEventAsync(request);

                        SendPacket(new StatusResponsePacket(status));
                    }
                    else if (packetData.Id == 0x01)
                    {
                        var pong = Net.Packets.Status.Serverbound.PingRequestPacket.Deserialize(packetData.NetworkBuffer.GetBuffer());

                        SendPacket(new PongResponsePacket { Timestamp = pong.Timestamp });
                    }
                    break;

                case ClientState.Handshaking:
                    if (packetData.Id != 0x00)
                        return;

                    await IntentionPacket.Deserialize(packetData.NetworkBuffer.GetBuffer()).HandleAsync(this);
                    break;

                case ClientState.Login:
                    await this.HandlePacketAsync(packetData);
                    break;
                case ClientState.Configuration:
                    Debug.Assert(Player is not null);

                    var result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player,
                        this.HostServer, packetData.Id, packetData.NetworkBuffer.GetBuffer()));

                    if (result == EventResult.Cancelled)
                        return;

                    await this.HandlePacketAsync(packetData);
                    break;
                case ClientState.Play:
                    Debug.Assert(Player is not null);

                    result = await this.eventDispatcher.ExecuteEventAsync(new PacketReceivedEventArgs(Player,
                        this.HostServer, packetData.Id, packetData.NetworkBuffer.GetBuffer()));

                    if (result == EventResult.Cancelled)
                        return;

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
            this.StartEncryptingOutput(sharedKey!);

            this.receiveBuffer.Reserve(MaxBufferSize);

            this.EnableCompression();
            this.SendPacket(new LoginFinishedPacket(Player.Uuid, Player.Username)
            {
                SkinProperties = this.Player.SkinProperties,
            });

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
            Log.SocketFailed(this.Logger, e.SocketError);
            this.Disconnect();
        }

        return false;
    }

    private bool ProcessSend(SocketAsyncEventArgs e)
    {
        lock (this.sendLock)
        {
            if (this.IsClosing || !this.Connected)
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

            if (e.SocketError == SocketError.Success && size > 0)
                return true;
        }

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
