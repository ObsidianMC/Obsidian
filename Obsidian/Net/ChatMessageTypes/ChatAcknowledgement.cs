namespace Obsidian.Net.ChatMessageTypes;

/// <summary>The client's offset, 20 acknowledged message bits, and checksum.</summary>
public readonly record struct ChatAcknowledgement(int Offset, int Acknowledged, byte Checksum)
{
    internal static ChatAcknowledgement Read(INetStreamReader reader)
    {
        var offset = reader.ReadVarInt();
        var bits = reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16;
        return new(offset, bits & 0xFFFFF, reader.ReadByte());
    }
}
