//     Obsidian/RconPacket.cs
//     Copyright (C) 2022

using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;

namespace Obsidian.Net.Rcon;

public class RconPacket
{
    private readonly Encoding encoding;

    public RconPacket(Encoding? encoding = null)
    {
        this.encoding = encoding ?? Encoding.ASCII;
    }

    public int Length => 4 + 4 + PayloadBytes.Length + 1; // RequestId (Int32) + Type (Int32) + PayloadBytes (varies) + padding (1)
    public int RequestId { get; set; }
    public RconPacketType Type { get; set; }
    public byte[] PayloadBytes { get; set; } = [0x00];
    public string PayloadText
    {
        get => PayloadBytes.Length > 1
            ? encoding.GetString(PayloadBytes.Take(PayloadBytes.Length - 1).ToArray())
            : string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value))
                PayloadBytes = [0x00];
            PayloadBytes = encoding.GetBytes(value).Append((byte)0x00).ToArray();
        }
    }

    public static RconPacket Read(Stream stream, Encoding? encoding = null)
    {
        var packet = new RconPacket(encoding);

        Span<byte> buf = stackalloc byte[4];

        stream.Read(buf);
        var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(buf) - 9;

        stream.Read(buf);
        packet.RequestId = BinaryPrimitives.ReadInt32LittleEndian(buf);

        stream.Read(buf);
        packet.Type = (RconPacketType)BinaryPrimitives.ReadInt32LittleEndian(buf);

        if (payloadSize > 0)
        {
            buf = payloadSize > 512 ? new byte[payloadSize] : stackalloc byte[payloadSize]; // Don't allocate more than 512 bytes on the stack
            stream.Read(buf);
            packet.PayloadBytes = buf.ToArray();
        }

        stream.ReadByte(); // Padding

        return packet;
    }

    public static async Task<RconPacket?> ReadAsync(Stream stream, CancellationToken ct, Encoding? encoding = null)
    {
        var packet = new RconPacket(encoding);

        var buf = new byte[4];

        await stream.ReadAsync(buf, ct);
        if (buf.All(x => x == 0x00)) return null;
        var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(buf) - 9;

        await stream.ReadAsync(buf, ct);
        packet.RequestId = BinaryPrimitives.ReadInt32LittleEndian(buf);

        await stream.ReadAsync(buf, ct);
        packet.Type = (RconPacketType)BinaryPrimitives.ReadInt32LittleEndian(buf);

        if (payloadSize > 0)
        {
            buf = new byte[payloadSize];
            await stream.ReadAsync(buf, ct);
            packet.PayloadBytes = buf.ToArray();
        }

        buf = new byte[1];
        await stream.ReadAsync(buf, ct);

        return packet;
    }

    public void Write(Stream stream)
    {
        Span<byte> buf = stackalloc byte[4];

        BinaryPrimitives.WriteInt32LittleEndian(buf, Length);
        stream.Write(buf);

        BinaryPrimitives.WriteInt32LittleEndian(buf, RequestId);
        stream.Write(buf);

        BinaryPrimitives.WriteInt32LittleEndian(buf, (int)Type);
        stream.Write(buf);

        stream.Write(PayloadBytes);

        stream.WriteByte(0x00); // Padding
    }

    public async Task WriteAsync(Stream stream, CancellationToken ct)
    {
        var buf = new byte[4];

        BinaryPrimitives.WriteInt32LittleEndian(buf, Length);
        await stream.WriteAsync(buf, ct);

        BinaryPrimitives.WriteInt32LittleEndian(buf, RequestId);
        await stream.WriteAsync(buf, ct);

        BinaryPrimitives.WriteInt32LittleEndian(buf, (int)Type);
        await stream.WriteAsync(buf, ct);

        if (PayloadBytes.Length > 0)
            await stream.WriteAsync(PayloadBytes, ct);

        buf = [0x00];
        await stream.WriteAsync(buf, ct); // Padding
    }
}

public enum RconPacketType
{
    CommandResponse = 0,
    Command = 2,
    Login = 3,

    EncryptedContent = 105,
    Upgrade = 222,
    EncryptStart = 223,
    EncryptTest = 224,
    EncryptSuccess = 225,
    EncryptInitial = 226,
    EncryptClientPublic = 227
}
