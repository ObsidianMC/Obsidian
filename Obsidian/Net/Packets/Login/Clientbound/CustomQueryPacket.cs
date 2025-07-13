using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;
public sealed partial class CustomQueryPacket
{
    [Field(0), VarLength]
    public required int MessageId { get; init; }

    [Field(1)]
    public required string Channel { get; init; }

    [Field(2)]
    public required byte[] Payload { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(this.Payload.Length, ServerConstants.MaxPayloadLength, "Payload");

        writer.WriteVarInt(this.MessageId);
        writer.WriteString(this.Channel);
        writer.WriteByteArray(this.Payload);
    }
}
