using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ContainerSetContentPacket(int containerId, List<ItemStack> items)
{
    [Field(0)]
    public int ContainerId { get; } = containerId;

    [Field(1), VarLength]
    public int StateId { get; set; }

    [Field(2)]
    public List<ItemStack> Items { get; } = items;

    [Field(3)]
    public ItemStack? CarriedItem { get; set; }

    public bool IsCarryingItem => this.CarriedItem != null;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ContainerId);
        writer.WriteVarInt(this.StateId);

        writer.WriteVarInt(this.Items.Count);

        foreach (var item in this.Items)
            writer.WriteItemStack(item);

        writer.WriteItemStack(this.CarriedItem);
    }
}
