using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class ClearTitlesPacket : IClientboundPacket
{
    [Field(0)]
    public bool Reset { get; init; }

    public int Id => 0x0C;
}
