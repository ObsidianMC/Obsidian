using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;

namespace Obsidian.Plugins;

/// <summary>
/// Runs the behaviour of a plugin's item, as a call into the plugin, so that the plugin can't be released meanwhile.
/// </summary>
internal static partial class ItemBehaviors
{
    // A player's inventory slots in the order their stacks tick, with the equipment slot each is (null for the main
    // inventory, and the hotbar's unless selected). Like vanilla's Inventory.tick: the hotbar and main inventory,
    // then the equipment.
    private static readonly (int Slot, EquipmentSlot? Equipment)[] tickOrder =
    [
        .. Enumerable.Range(36, 9).Select(slot => (slot, (EquipmentSlot?)null)),
        .. Enumerable.Range(9, 27).Select(slot => (slot, (EquipmentSlot?)null)),
        (45, EquipmentSlot.OffHand),
        (8, EquipmentSlot.Boots),
        (7, EquipmentSlot.Leggings),
        (6, EquipmentSlot.Chestplate),
        (5, EquipmentSlot.Helmet)
    ];

    /// <summary>
    /// Whether a player may use items at all, which vanilla checks before an item does anything: a spectator or a dead
    /// player can't.
    /// </summary>
    public static bool CanUseItems(IPlayer player) => player.Health > 0 && player.GameMode != GameMode.Spectator;

    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseAsync(ItemUseContext context) =>
        RunUseAsync(context.Player, context.Stack.Holder, behavior => behavior.UseAsync(context));

    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseOnBlockAsync(ItemUseOnBlockContext context) =>
        RunUseAsync(context.Player, context.Stack.Holder, behavior => behavior.UseOnBlockAsync(context));

    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseOnEntityAsync(ItemUseOnEntityContext context) =>
        RunUseAsync(context.Player, context.Stack.Holder, behavior => behavior.UseOnEntityAsync(context));

    /// <summary>
    /// Uses the stack <paramref name="player"/> holds in <paramref name="hand"/> on <paramref name="entity"/>, when
    /// it's a plugin's item and the player may use items.
    /// </summary>
    /// <remarks>
    /// Like vanilla's <c>Player.interactOn</c>, a creative player's item gets a copy of the stack, so that the use
    /// leaves the player's stack as it was.
    /// </remarks>
    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseOnEntityAsync(IPlayer player, InteractionHand hand, ILiving entity)
    {
        var stack = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (stack is not { Count: > 0 } || stack.Holder.Behavior is null ||!CanUseItems(player))
            return ValueTask.FromResult(false);

        var context = new ItemUseOnEntityContext
        {
            Player = player,
            Stack = player.GameMode == GameMode.Creative ? new ItemStack(stack, stack.Count) : stack,
            Hand = hand,
            Entity = entity
        };

        return UseOnEntityAsync(context);
    }

    /// <summary>
    /// Whether <paramref name="inventory"/> holds a stack whose item ticks in inventories, read without its lock:
    /// cheap, and false at once when no item ticks there.
    /// </summary>
    public static bool HasInventoryTicks(BaseContainer inventory)
    {
        if (!ItemsRegistry.AnyTicksInInventory)
            return false;

        foreach (var (slot, _) in tickOrder)
        {
            if (inventory.GetItem(slot)?.Holder.Binding is { TicksInInventory: true })
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ticks the stacks in <paramref name="player"/>'s inventory whose items tick there, until one kills the player.
    /// The caller holds the inventory's lock, in a <see cref="Entities.DeferredInventoryWork"/> section. A hook that
    /// throws is logged with <paramref name="logger"/>, so that the tick goes on.
    /// </summary>
    /// <param name="selectedSlot">The player's selected hotbar slot, 36 to 44.</param>
    public static async ValueTask TickInventoryAsync(
        IPlayer player,
        BaseContainer inventory,
        int selectedSlot,
        ILogger logger)
    {
        foreach (var (slot, equipment) in tickOrder)
        {
            // A hook that killed the player ends the tick, as vanilla's death empties the inventory at once, so that
            // the remaining stacks don't tick; here the drops wait for the lock (see DeferredInventoryWork).
            if (player.Health <= 0)
                break;

            if (inventory.GetItem(slot) is not { Count: > 0 } stack
                || stack.Holder.Binding is not { TicksInInventory: true })
            {
                continue;
            }

            var context = new ItemInventoryTickContext
            {
                Player = player,
                Stack = stack,
                Slot = slot,
                EquipmentSlot = slot == selectedSlot ? EquipmentSlot.MainHand : equipment
            };

            try
            {
                await RunAsync(stack.Holder, async behavior =>
                {
                    await behavior.InventoryTickAsync(context);
                    return true;
                });
            }
            catch (Exception exception)
            {
                InventoryTickFailed(logger, exception, stack.Holder.UnlocalizedName);
            }
        }
    }

    private static async ValueTask<bool> RunUseAsync(
        IPlayer player, Item item, Func<IItemBehavior, ValueTask<bool>> use)
    {
        if (item.Binding is not { Behavior: not null })
            return false;

        var used = player is Entities.Player concrete
            ? await concrete.RunItemUseAsync(() => RunAsync(item, use))
            : await RunAsync(item, use);
        if (used && player is Entities.Player statisticsPlayer)
            statisticsPlayer.Statistics.Increment(item.UnlocalizedName, type: "minecraft:used");

        return used;
    }

    private static async ValueTask<bool> RunAsync(Item item, Func<IItemBehavior, ValueTask<bool>> use)
    {
        if (item.Binding is not { Behavior: IItemBehavior behavior, Owner: PluginContainer plugin })
            return false;

        using var call = plugin.TryEnter();
        if (call is null)
            return false;

        return await use(behavior);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Item}'s inventory tick hook failed")]
    private static partial void InventoryTickFailed(ILogger logger, Exception exception, string item);
}
