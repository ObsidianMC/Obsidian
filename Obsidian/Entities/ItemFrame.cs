using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item_frame")]
public partial class ItemFrame : Entity
{
    public ItemStack? Item { get; private set; }

    public int Rotation { get; private set; }
}
