using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class PlayerInfoRemovePacket : IClientboundPacket
{
    [Field(0)]
    public List<Guid> UUIDs { get; init; }

    public int Id => 0x3D;
}
