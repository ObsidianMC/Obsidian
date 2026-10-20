using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Plugins;

/// <summary>
/// Runs the behaviour of a plugin's block, as a call into the plugin, so that the plugin can't be released meanwhile.
/// </summary>
internal static partial class BlockBehaviors
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
    public static IBlock? GetStateForPlacement(BlockPlaceContext context, ILogger logger)
    {
        if (!TryEnter(context.Block, out var behavior, out var call))
            return PlacementBehaviors.For(context.Block)?.GetStateForPlacement(context) ?? context.Block;

        using (call)
        {
            try
            {
                return behavior.GetStateForPlacement(context);
            }
            catch (Exception exception)
            {
                HookFailed(
                    logger, exception, context.Block.UnlocalizedName, nameof(IBlockBehavior.GetStateForPlacement));
                return null;
            }
        }
    }

    /// <summary>
    /// Runs the block's <see cref="IBlockBehavior.EntityInsideAsync"/>. One that throws is logged with
    /// <paramref name="logger"/>, so that the level's tick goes on.
    /// </summary>
    public static async ValueTask EntityInsideAsync(EntityBlockContext context, ILogger logger)
    {
        if (!TryEnter(context.State, out var behavior, out var call))
            return;

        using (call)
        {
            try
            {
                await behavior.EntityInsideAsync(context);
            }
            catch (Exception exception)
            {
                var hook = nameof(IBlockBehavior.EntityInsideAsync);
                HookFailed(logger, exception, context.State.UnlocalizedName, hook);
            }
        }
    }

    /// <summary>
    /// Runs the block's <see cref="IBlockBehavior.SteppedOnAsync"/>. One that throws is logged with
    /// <paramref name="logger"/>, so that the level's tick goes on.
    /// </summary>
    public static async ValueTask SteppedOnAsync(EntityBlockContext context, ILogger logger)
    {
        if (!TryEnter(context.State, out var behavior, out var call))
            return;

        using (call)
        {
            try
            {
                await behavior.SteppedOnAsync(context);
            }
            catch (Exception exception)
            {
                var hook = nameof(IBlockBehavior.SteppedOnAsync);
                HookFailed(logger, exception, context.State.UnlocalizedName, hook);
            }
        }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "{Block}'s {Hook} hook failed")]
    private static partial void HookFailed(ILogger logger, Exception exception, string block, string hook);
}
