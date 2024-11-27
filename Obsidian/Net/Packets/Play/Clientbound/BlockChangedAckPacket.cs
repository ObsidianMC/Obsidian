using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class BlockChangedAckPacket
{
    [Field(0), VarLength]
    public int SequenceID { get; init; }

    public override void Serialize(INetStreamWriter writer) => writer.WriteVarInt(SequenceID);
}
