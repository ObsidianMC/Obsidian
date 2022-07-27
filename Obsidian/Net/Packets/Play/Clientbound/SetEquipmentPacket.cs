using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetEquipmentPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public List<Equipment> Equipment { get; init; }

    public int Id => 0x50;
}
