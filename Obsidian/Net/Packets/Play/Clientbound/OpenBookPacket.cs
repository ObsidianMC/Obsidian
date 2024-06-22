using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class OpenBookPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; set; }

    public int Id => 0x32;
}
