using Obsidian.Entities;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    // The client opens this editor itself using the structure block's ordinary chunk/update tag.
    internal static IBlockUseBehavior StructureBlock { get; } = new StructureBlockUse();

    private sealed class StructureBlockUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use) => ValueTask.FromResult(
            use.BlockEntity is StructureBlockEntity && CommandBlockMinecart.CanEdit(use.Player)
                ? UseResult.Success
                : UseResult.Pass);
    }
}
