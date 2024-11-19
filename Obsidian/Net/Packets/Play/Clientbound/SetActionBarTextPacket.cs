using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetActionBarTextPacket
{
    [Field(0)]
    public required string Text { get; init; }
}
