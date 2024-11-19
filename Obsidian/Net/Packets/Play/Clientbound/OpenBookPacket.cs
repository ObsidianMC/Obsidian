using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class OpenBookPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; set; }
}
