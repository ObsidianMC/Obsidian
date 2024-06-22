using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetEquipmentPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public List<Equipment> Equipment { get; init; }

    public int Id => 0x5B;

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();
        packetStream.WriteVarInt(EntityId);

        var count = this.Equipment.Count;
        for (int i = 0; i < count; i++)
        {
            var equipment = Equipment[i];

            var val = i == count - 1 ? (sbyte)equipment.Slot : (sbyte)((sbyte)equipment.Slot | 128);

            packetStream.WriteByte(val);
            packetStream.WriteItemStack(equipment.Item);
        }

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
