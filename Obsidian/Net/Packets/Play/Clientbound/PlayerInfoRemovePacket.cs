using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class PlayerInfoRemovePacket
{
    [Field(0)]
    public List<Guid> UUIDs { get; init; }
}
