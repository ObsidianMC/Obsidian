using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetEquipmentPacket 
{
    [Field(0), VarLength]
    public required int EntityId { get; init; }

    [Field(1)]
    public required List<Equipment> Equipment { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(EntityId);

        var count = this.Equipment.Count;
        for (int i = 0; i < count; i++)
        {
            var equipment = Equipment[i];

            var val = i == count - 1 ? (sbyte)equipment.Slot : (sbyte)((sbyte)equipment.Slot | 128);

            writer.WriteByte(val);
            writer.WriteItemStack(equipment.Item);
        }
    }
}
