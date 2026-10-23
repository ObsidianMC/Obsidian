using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;

namespace Obsidian.LoadTest;

/// <summary>
/// A bot's TCP connection, framed like vanilla's protocol: each packet is prefixed with its length and, once the server
/// enables compression, packets at or above its threshold are zlib-compressed. Bots log in offline, so there's no
/// encryption. Packets are received on one task and may be sent from any thread.
/// </summary>
internal sealed class BotConnection : IDisposable
{
    private const int MaxFrameLength = (1 << 21) - 1;

    private readonly TcpClient tcpClient;
    private readonly NetworkStream stream;
    private readonly BufferedStream input;
    private readonly Lock sendLock = new();
    private readonly LoadStats stats;

    // -1 until the server enables compression.
    private volatile int compressionThreshold = -1;

    private BotConnection(TcpClient tcpClient, LoadStats stats)
    {
        this.tcpClient = tcpClient;
        this.stream = tcpClient.GetStream();
        this.input = new BufferedStream(this.stream, 64 * 1024);
        this.stats = stats;
    }

    public static async Task<BotConnection> ConnectAsync(string host, int port, LoadStats stats, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient { NoDelay = true };
        try
        {
            await tcpClient.ConnectAsync(host, port, cancellationToken);
            return new BotConnection(tcpClient, stats);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    public void SetCompressionThreshold(int threshold) => this.compressionThreshold = threshold;

    /// <summary>Waits for the next packet, and returns its id and a reader positioned at its data.</summary>
    public async ValueTask<(int Id, PacketReader Data)> ReceiveAsync(CancellationToken cancellationToken)
    {
        var frameLength = await this.ReadFrameLengthAsync(cancellationToken);
        if (frameLength is <= 0 or > MaxFrameLength)
            throw new InvalidDataException($"Invalid packet length {frameLength}.");

        var frame = new byte[frameLength];
        await this.input.ReadExactlyAsync(frame, cancellationToken);
        this.stats.AddReceivedBytes(frameLength);

        var packet = new PacketReader(this.compressionThreshold >= 0 ? Decompress(frame) : frame);
        return (packet.ReadVarInt(), packet);
    }

    /// <summary>Sends a packet whose data <paramref name="write"/> writes.</summary>
    public void Send(int id, Action<PacketWriter>? write = null)
    {
        var packet = new PacketWriter();
        packet.WriteVarInt(id);
        write?.Invoke(packet);

        var frame = this.Frame(packet.Written);
        lock (this.sendLock)
            this.stream.Write(frame);
    }

    public void Dispose()
    {
        this.input.Dispose();
        this.tcpClient.Dispose();
    }

    private byte[] Frame(ReadOnlySpan<byte> packet)
    {
        var body = new PacketWriter();
        var threshold = this.compressionThreshold;
        if (threshold < 0)
        {
            body.Write(packet);
        }
        else if (packet.Length < threshold)
        {
            // A data length of 0 marks a packet below the threshold as uncompressed.
            body.WriteVarInt(0);
            body.Write(packet);
        }
        else
        {
            body.WriteVarInt(packet.Length);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(packet);
            body.Write(compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }

        var frame = new PacketWriter();
        frame.WriteVarInt(body.Written.Length);
        frame.Write(body.Written);
        return frame.Written.ToArray();
    }

    // A compressed frame starts with the packet's uncompressed length, or 0 when it isn't compressed.
    private static byte[] Decompress(byte[] frame)
    {
        var header = new PacketReader(frame);
        var dataLength = header.ReadVarInt();
        if (dataLength == 0)
            return frame[header.Position..];

        var data = new byte[dataLength];
        using var zlib = new ZLibStream(new MemoryStream(frame, header.Position, frame.Length - header.Position), CompressionMode.Decompress);
        zlib.ReadExactly(data);
        return data;
    }

    private async ValueTask<int> ReadFrameLengthAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (await this.input.ReadAsync(buffer, cancellationToken) == 0)
                throw new EndOfStreamException("The server closed the connection.");

            value |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("A packet length VarInt is too long.");
    }
}

/// <summary>Writes the protocol's data types, big-endian.</summary>
internal sealed class PacketWriter
{
    private byte[] buffer = new byte[64];
    private int length;

    public ReadOnlySpan<byte> Written => this.buffer.AsSpan(0, this.length);

    public void Write(ReadOnlySpan<byte> data) => data.CopyTo(this.Take(data.Length));

    public void WriteByte(byte value) => this.Take(1)[0] = value;

    public void WriteBoolean(bool value) => this.WriteByte(value ? (byte)1 : (byte)0);

    public void WriteInt(int value) => BinaryPrimitives.WriteInt32BigEndian(this.Take(4), value);

    public void WriteLong(long value) => BinaryPrimitives.WriteInt64BigEndian(this.Take(8), value);

    public void WriteFloat(float value) => BinaryPrimitives.WriteSingleBigEndian(this.Take(4), value);

    public void WriteDouble(double value) => BinaryPrimitives.WriteDoubleBigEndian(this.Take(8), value);

    public void WriteUnsignedShort(ushort value) => BinaryPrimitives.WriteUInt16BigEndian(this.Take(2), value);

    public void WriteVarInt(int value)
    {
        var unsigned = (uint)value;
        do
        {
            var next = (byte)(unsigned & 0x7F);
            unsigned >>= 7;
            this.WriteByte(unsigned != 0 ? (byte)(next | 0x80) : next);
        }
        while (unsigned != 0);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        this.WriteVarInt(bytes.Length);
        this.Write(bytes);
    }

    public void WriteUuid(Guid value)
    {
        // Java's UUID order: the most significant long first, both big-endian.
        value.TryWriteBytes(this.Take(16), bigEndian: true, out _);
    }

    private Span<byte> Take(int count)
    {
        if (this.length + count > this.buffer.Length)
            Array.Resize(ref this.buffer, Math.Max(this.buffer.Length * 2, this.length + count));

        var span = this.buffer.AsSpan(this.length, count);
        this.length += count;
        return span;
    }
}

/// <summary>Reads the protocol's data types, big-endian.</summary>
internal sealed class PacketReader(byte[] data)
{
    public int Position { get; private set; }

    public byte ReadByte() => data[this.Position++];

    public bool ReadBoolean() => this.ReadByte() != 0;

    public int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(this.Take(4));

    public long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(this.Take(8));

    public float ReadFloat() => BinaryPrimitives.ReadSingleBigEndian(this.Take(4));

    public double ReadDouble() => BinaryPrimitives.ReadDoubleBigEndian(this.Take(8));

    public int ReadVarInt()
    {
        var value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var next = this.ReadByte();
            value |= (next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("A VarInt is too long.");
    }

    public string ReadString() => Encoding.UTF8.GetString(this.Take(this.ReadVarInt()));

    private ReadOnlySpan<byte> Take(int count)
    {
        var span = data.AsSpan(this.Position, count);
        this.Position += count;
        return span;
    }
}
