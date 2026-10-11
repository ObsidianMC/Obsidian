using Obsidian.API.Blocks;

namespace Obsidian.Plugins;

/// <summary>
/// Runs the behaviour of a plugin's block, as a call into the plugin, so that the plugin can't be released meanwhile.
/// </summary>
internal static class BlockBehaviors
{
    /// <returns>Whether the block's behaviour handled the use.</returns>
    public static async ValueTask<bool> UseAsync(BlockUseContext context)
    {
        if (!BlocksRegistry.TryGetPluginBlock(context.Block.GetHashCode(), out var type)
            || type.Binding is not { Behavior: IBlockBehavior behavior, Owner: PluginContainer plugin })
        {
            return false;
        }

        using var call = plugin.TryEnter();
        if (call is null)
            return false;

        return await behavior.UseAsync(context);
    }
}
