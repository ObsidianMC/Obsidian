using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetActionBarTextPacket : IClientboundPacket
{
    [Field(0)]
    public required string Text { get; init; }

    public int Id => 0x4B;
}
