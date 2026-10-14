using Obsidian.API.Blocks;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Plugins;

/// <summary>
/// Runs the behaviour of a plugin's block, as a call into the plugin, so that the plugin can't be released meanwhile.
/// </summary>
internal static class BlockBehaviors
{
    /// <returns>Whether the block's behaviour handled the use.</returns>
    public static async ValueTask<bool> UseAsync(BlockUseContext context)
    {
        if (!TryEnter(context.Block, out var behavior, out var call))
            return false;

        using (call)
            return await behavior.UseAsync(context);
    }

    /// <returns>
    /// The state the player places, or null to not place it: the block's default state for blocks without behaviour.
    /// </returns>
    public static IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        if (!TryEnter(context.Block, out var behavior, out var call))
            return context.Block;

        using (call)
            return behavior.GetStateForPlacement(context);
    }

    public static async ValueTask EntityInsideAsync(EntityBlockContext context)
    {
        if (!TryEnter(context.State, out var behavior, out var call))
            return;

        using (call)
            await behavior.EntityInsideAsync(context);
    }

    public static async ValueTask SteppedOnAsync(EntityBlockContext context)
    {
        if (!TryEnter(context.State, out var behavior, out var call))
            return;

        using (call)
            await behavior.SteppedOnAsync(context);
    }

    /// <summary>
    /// Whether <paramref name="block"/> is a plugin's block with a behaviour, bound to a loaded plugin.
    /// </summary>
    public static bool HasBehavior(IBlock block) => Binding(block) is { Behavior: not null };

    /// <summary>
    /// Whether <paramref name="block"/> is a plugin's block whose behaviour gets random ticks.
    /// </summary>
    public static bool IsRandomlyTicking(IBlock block) =>
        Binding(block) is { Behavior: not null, RandomlyTicks: true };

    /// <summary>
    /// Starts a call into the plugin whose block <paramref name="block"/> is, for running its behaviour.
    /// </summary>
    /// <returns>
    /// Whether the block has a behaviour and its plugin isn't unloading; then dispose the call once it ends.
    /// </returns>
    public static bool TryEnter(
        IBlock block,
        [NotNullWhen(true)] out IBlockBehavior? behavior,
        [NotNullWhen(true)] out PluginCall? call)
    {
        call = null;
        behavior = null;

        if (Binding(block) is not { Behavior: IBlockBehavior bound, Owner: PluginContainer plugin })
            return false;

        call = plugin.TryEnter();
        if (call is null)
            return false;

        behavior = bound;
        return true;
    }

    private static BlockBinding? Binding(IBlock block) =>
        BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type) ? type.Binding : null;
}
