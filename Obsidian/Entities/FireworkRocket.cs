using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:firework_rocket")]
public sealed partial class FireworkRocket : Entity
{
    public ItemStack? Item { get; private set; }
    public int Rotation { get; private set; }
}
