namespace Obsidian.API.Inventory;

/// <summary>
/// What a plugin's item does when a player uses it. Implement the methods the item needs; the others leave the item
/// to Obsidian's default handling, which for an item without vanilla behaviour is nothing.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while the item stays registered (stacks
/// of it stay in inventories and keep their id), so keep state in the item stack's components or the plugin, not here.
/// </remarks>
public interface IItemBehavior
{
    /// <summary>
    /// Called when a player uses the item without aiming at a block (right-clicking the air).
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips Obsidian's own handling.</returns>
    public ValueTask<bool> UseAsync(ItemUseContext context) => ValueTask.FromResult(false);

    /// <summary>
    /// Called when a player uses the item on a block (right-clicking it), before the block reacts or the item places a
    /// block.
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips Obsidian's own handling.</returns>
    public ValueTask<bool> UseOnBlockAsync(ItemUseOnBlockContext context) => ValueTask.FromResult(false);

    /// <summary>
    /// Called when a player uses the item on a living entity (right-clicks it), on the entity's level tick, after the
    /// entity's own interaction (a villager's trading, feeding an animal, a plugin entity type's behaviour) and only
    /// when that did nothing, like vanilla's <c>interactLivingEntity</c>.
    /// </summary>
    /// <remarks>
    /// Mobs that can be ridden (tamed horses, camels, adult happy ghasts and nautiluses) give the item its use during
    /// their own interaction, before the player mounts, as vanilla's do; so when it returns false and the mob does
    /// nothing either, it's called twice in one use. In creative mode the context's stack is a copy, so that changing
    /// it doesn't change the player's.
    /// </remarks>
    /// <returns>Whether the behaviour handled the use.</returns>
    public ValueTask<bool> UseOnEntityAsync(ItemUseOnEntityContext context) => ValueTask.FromResult(false);

    /// <summary>
    /// Called every tick, on the player's tick, for each stack of the item in a player's inventory: their hotbar and
    /// main inventory, then their off hand and armour, like vanilla's <c>inventoryTick</c>.
    /// </summary>
    /// <remarks>
    /// It runs holding the player's inventory lock, which clicks take too, so that it never sees a click half done.
    /// Change the stack or the inventory through their own members (a slot changed this way is sent to the player
    /// after the tick), and don't wait on anything that clicks or the inventory lock wait on. Players' inventories
    /// are only scanned for items whose behaviour implements this method, so leave it out when the item doesn't
    /// need it.
    /// </remarks>
    public ValueTask InventoryTickAsync(ItemInventoryTickContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// A player using an item.
/// </summary>
public class ItemUseContext
{
    public required IPlayer Player { get; init; }

    /// <summary>
    /// The stack the player uses.
    /// </summary>
    public required ItemStack Stack { get; init; }

    public required InteractionHand Hand { get; init; }
}

/// <summary>
/// A player using an item on a block.
/// </summary>
public sealed class ItemUseOnBlockContext : ItemUseContext
{
    public required Vector Position { get; init; }

    public required BlockFace Face { get; init; }

    public required IBlock Block { get; init; }
}

/// <summary>
/// A player using an item on a living entity.
/// </summary>
public sealed class ItemUseOnEntityContext : ItemUseContext
{
    public required ILiving Entity { get; init; }
}

/// <summary>
/// A stack of an item ticking in a player's inventory.
/// </summary>
public sealed class ItemInventoryTickContext
{
    public required IPlayer Player { get; init; }

    public required ItemStack Stack { get; init; }

    /// <summary>
    /// The stack's slot in the player's inventory: 5 to 8 for the head, chest, legs and feet, 9 to 35 for the main
    /// inventory, 36 to 44 for the hotbar and 45 for the off hand.
    /// </summary>
    public required int Slot { get; init; }

    /// <summary>
    /// The equipment slot the stack is in: <see cref="Inventory.EquipmentSlot.MainHand"/> for the selected hotbar
    /// slot, the off hand or an armour slot; null elsewhere in the inventory.
    /// </summary>
    public EquipmentSlot? EquipmentSlot { get; init; }

    /// <summary>
    /// Whether the stack is in the player's selected hotbar slot, their main hand.
    /// </summary>
    public bool IsSelected => this.EquipmentSlot == Inventory.EquipmentSlot.MainHand;
}
