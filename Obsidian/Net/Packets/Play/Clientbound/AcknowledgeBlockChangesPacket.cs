using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class AcknowledgeBlockChangesPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int SequenceID { get; init; }

    public int Id => 0x05;
}
