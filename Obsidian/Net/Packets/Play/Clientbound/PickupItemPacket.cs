using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PickupItemPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int CollectedEntityId { get; init; }

    [Field(1), VarLength]
    public int CollectorEntityId { get; init; }

    [Field(2), VarLength]
    public int PickupItemCount { get; init; }

    public int Id => 0x6F;
}
