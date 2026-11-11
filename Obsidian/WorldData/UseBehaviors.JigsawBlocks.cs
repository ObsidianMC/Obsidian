using Obsidian.Entities;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    // As with structure blocks, the client opens the editor from the ordinary block entity update tag.
    internal static IBlockUseBehavior JigsawBlock { get; } = new JigsawUse();

    private sealed class JigsawUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use) => ValueTask.FromResult(
            use.BlockEntity is JigsawBlockEntity && CommandBlockMinecart.CanEdit(use.Player)
                ? UseResult.Success : UseResult.Pass);
    }
}
