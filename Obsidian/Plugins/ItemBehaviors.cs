using Obsidian.API.Inventory;

namespace Obsidian.Plugins;

/// <summary>
/// Runs the behaviour of a plugin's item, as a call into the plugin, so that the plugin can't be released meanwhile.
/// </summary>
internal static class ItemBehaviors
{
    /// <summary>
    /// Whether a player may use items at all, which vanilla checks before an item does anything: a spectator or a dead
    /// player can't.
    /// </summary>
    public static bool CanUseItems(IPlayer player) => player.Health > 0 && player.GameMode != GameMode.Spectator;

    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseAsync(ItemUseContext context) =>
        RunAsync(context.Stack.Holder, behavior => behavior.UseAsync(context));

    /// <returns>Whether the item's behaviour handled the use.</returns>
    public static ValueTask<bool> UseOnBlockAsync(ItemUseOnBlockContext context) =>
        RunAsync(context.Stack.Holder, behavior => behavior.UseOnBlockAsync(context));

    private static async ValueTask<bool> RunAsync(Item item, Func<IItemBehavior, ValueTask<bool>> use)
    {
        if (item.Binding is not { Behavior: IItemBehavior behavior, Owner: PluginContainer plugin })
            return false;

        using var call = plugin.TryEnter();
        if (call is null)
            return false;

        return await use(behavior);
    }
}
