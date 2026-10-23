using Obsidian.API.Inventory;
namespace Obsidian.Entities;

/// <summary>
/// A minecart with a chest, like vanilla's <c>MinecartChest</c>: 27 slots, as world generation leaves them in
/// mineshafts with a loot table that fills them when they're first opened.
/// </summary>
[MinecraftEntity("minecraft:chest_minecart")]
public sealed partial class ChestMinecart : AbstractMinecartContainer
{
    public ChestMinecart() : base(27, "Minecart with Chest") => this.Type = EntityType.ChestMinecart;

    protected override int DefaultDisplayOffset => 8;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.ChestMinecart);
}
