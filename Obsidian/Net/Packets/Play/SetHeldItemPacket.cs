using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetHeldItemPacket : IServerboundPacket, IClientboundPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public int Id { get; }

    public SetHeldItemPacket(bool toClient)
    {
        this.Id = toClient ? 0x53 : 0x2F;
    }

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteByte((sbyte)Slot);

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }

    public static SetHeldItemPacket Deserialize(MinecraftStream stream)
    {
        var packet = new SetHeldItemPacket(false);
        packet.Populate(stream);
        return packet;
    }

    public ValueTask HandleAsync(Server server, Player player)
    {
        player.CurrentSlot = Slot;

        var heldItem = player.GetHeldItem();

        player.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEquipmentPacket
        {
            EntityId = player.EntityId,
            Equipment = new()
            {
                new()
                {
                    Slot = EquipmentSlot.MainHand,
                    Item = heldItem
                }
            }
        }, player.EntityId);

        return ValueTask.CompletedTask;
    }
}
