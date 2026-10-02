using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// State of one <see cref="TreeFeature"/> placement, passed to the trunk, foliage and root placers.
/// </summary>
/// <remarks>
/// Mirrors the setters vanilla's <c>TreeFeature.place</c> hands to its placers: every placed block is written to the
/// level and its position recorded in the matching set (roots, logs, foliage, decorations), whose Java iteration order
/// later drives the decorators and the leaf distance pass. Positions are recorded even if the write is rejected.
/// </remarks>
public sealed class TreeContext
{
    internal TreeContext(TreeFeature config, IWorldGenLevel level, IRandomSource random, WorldGenerationContext generation)
    {
        this.Config = config;
        this.Level = level;
        this.Random = random;
        this.Generation = generation;
    }

    public TreeFeature Config { get; }

    public IWorldGenLevel Level { get; }

    public IRandomSource Random { get; }

    public WorldGenerationContext Generation { get; }

    internal VanillaBlockPosSet Roots { get; } = new();

    internal VanillaBlockPosSet Logs { get; } = new();

    internal VanillaBlockPosSet Foliage { get; } = new();

    internal VanillaBlockPosSet Decorations { get; } = new();

    /// <summary>Places a trunk block (logs and the dirt under the trunk).</summary>
    public void SetLog(Vector position, IBlock block)
    {
        this.Logs.Add(position);
        this.Level.SetBlock(position, block);
    }

    public void SetRoot(Vector position, IBlock block)
    {
        this.Roots.Add(position);
        this.Level.SetBlock(position, block);
    }

    /// <summary>Vanilla <c>FoliageSetter.set</c>.</summary>
    public void SetFoliage(Vector position, IBlock block)
    {
        this.Foliage.Add(position);
        this.Level.SetBlock(position, block);
    }

    /// <summary>Vanilla <c>FoliageSetter.isSet</c>: whether this tree already placed foliage at the position.</summary>
    public bool IsFoliageSet(Vector position) => this.Foliage.Contains(position);

    internal void SetDecoration(Vector position, IBlock block)
    {
        this.Decorations.Add(position);
        this.Level.SetBlock(position, block);
    }

    /// <summary>Gives the storage of the tree's position sets back to the thread's pool once the tree is done.</summary>
    internal void Release()
    {
        this.Roots.Release();
        this.Logs.Release();
        this.Foliage.Release();
        this.Decorations.Release();
    }
}
