using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class CooldownPacket
{
    [Field(0), VarLength]
    public int ItemId { get; init; }

    [Field(1), VarLength]
    public int CooldownTicks { get; init; }
}
