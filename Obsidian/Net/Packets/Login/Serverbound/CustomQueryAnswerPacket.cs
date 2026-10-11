using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;
public partial class CustomQueryAnswerPacket
{
    [Field(0)]
    [VarLength]
    public int MessageId { get; private set; }

    [Field(1)]
    public bool Successful { get; private set; }

    [Field(2)]
    [Condition("Successful")]
    public ReadOnlyMemory<byte> Data { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.MessageId = reader.ReadVarInt();
        this.Successful = reader.ReadBoolean();

        // Like vanilla, the payload is the rest of the packet, up to its payload limit.
        this.Data = this.Successful ? reader.ReadRemainingBytes(ServerConstants.MaxPayloadLength) : ReadOnlyMemory<byte>.Empty;
    }
}
