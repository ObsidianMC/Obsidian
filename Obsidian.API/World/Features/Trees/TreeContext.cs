using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features.Trees;

/// <summary>
/// One tree being placed by a <c>minecraft:tree</c> feature, which it passes to its trunk, foliage and root placers.
/// </summary>
/// <remarks>
/// Like the setters vanilla's <c>TreeFeature.place</c> hands to its placers, every block set through the context is
/// written to the level and recorded as part of the tree, for its decorators and the leaf distance update that
/// follow. Only the server creates contexts.
/// </remarks>
public abstract class TreeContext
{
    internal TreeContext()
    {
    }

    public abstract IWorldGenLevel Level { get; }

    public abstract IRandomSource Random { get; }

    public abstract WorldGenerationContext Generation { get; }

    /// <summary>The tree feature's <c>trunk_provider</c>, for logs.</summary>
    public abstract IBlockStateProvider TrunkProvider { get; }

    /// <summary>The tree feature's <c>foliage_provider</c>, for leaves.</summary>
    public abstract IBlockStateProvider FoliageProvider { get; }

    /// <summary>The tree feature's <c>dirt_provider</c>, placed under the trunk.</summary>
    public abstract IBlockStateProvider DirtProvider { get; }

    /// <summary>
    /// Whether the block under the trunk is replaced with <see cref="DirtProvider"/> even when it's dirt.
    /// </summary>
    public abstract bool ForceDirt { get; }

    /// <summary>Places a trunk block: a log, or the dirt under the trunk.</summary>
    public abstract void SetLog(Vector position, IBlock block);

    public abstract void SetRoot(Vector position, IBlock block);

    /// <summary>Vanilla <c>FoliageSetter.set</c>.</summary>
    public abstract void SetFoliage(Vector position, IBlock block);

    /// <summary>Vanilla <c>FoliageSetter.isSet</c>: whether this tree already placed foliage at the position.</summary>
    public abstract bool IsFoliageSet(Vector position);
}
