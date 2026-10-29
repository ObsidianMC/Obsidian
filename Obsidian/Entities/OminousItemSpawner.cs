using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ominous_item_spawner")]
public sealed partial class OminousItemSpawner : Entity
{
    public OminousItemSpawner() => Type = EntityType.OminousItemSpawner;

    public ItemStack? Item { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(Item);
    }
}
