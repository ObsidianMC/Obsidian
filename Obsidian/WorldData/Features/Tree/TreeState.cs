using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// The state of one <see cref="TreeFeature"/> placement, passed to its trunk, foliage and root placers.
/// </summary>
/// <remarks>
/// Each placed block's position is recorded in the matching set (roots, logs, foliage, decorations), even if the write
/// is rejected; the sets' vanilla iteration order later drives the decorators and the leaf distance pass.
/// </remarks>
internal sealed class TreeState : TreeContext
{
    public TreeState(TreeFeature config, IWorldGenLevel level, IRandomSource random, WorldGenerationContext generation)
    {
        this.Config = config;
        this.Level = level;
        this.Random = random;
        this.Generation = generation;
    }

    public TreeFeature Config { get; }

    public override IWorldGenLevel Level { get; }

    public override IRandomSource Random { get; }

    public override WorldGenerationContext Generation { get; }

    public override IBlockStateProvider TrunkProvider => this.Config.TrunkProvider;

    public override IBlockStateProvider FoliageProvider => this.Config.FoliageProvider;

    public override IBlockStateProvider DirtProvider => this.Config.DirtProvider;

    public override bool ForceDirt => this.Config.ForceDirt;

    public VanillaBlockPosSet Roots { get; } = new();

    public VanillaBlockPosSet Logs { get; } = new();

    public VanillaBlockPosSet Foliage { get; } = new();

    public VanillaBlockPosSet Decorations { get; } = new();

    public override void SetLog(Vector position, IBlock block)
    {
        this.Logs.Add(position);
        this.Level.SetBlock(position, block);
    }

    public override void SetRoot(Vector position, IBlock block)
    {
        this.Roots.Add(position);
        this.Level.SetBlock(position, block);
    }

    public override void SetFoliage(Vector position, IBlock block)
    {
        this.Foliage.Add(position);
        this.Level.SetBlock(position, block);
    }

    public override bool IsFoliageSet(Vector position) => this.Foliage.Contains(position);

    /// <summary>What the tree's decorators see of it.</summary>
    public TreeDecoratorContext ForDecorators() => new DecoratorView(this);

    /// <summary>
    /// Gives the storage of the tree's position sets back to the thread's pool once the tree is done.
    /// </summary>
    public void Release()
    {
        this.Roots.Release();
        this.Logs.Release();
        this.Foliage.Release();
        this.Decorations.Release();
    }

    // A stable sort by Y, like fastutil's ObjectArrayList.sort used by vanilla: the keys put the set's order after Y.
    private static Vector[] SortedByY(VanillaBlockPosSet positions)
    {
        var sorted = new Vector[positions.Count];
        var keys = new long[sorted.Length];
        var index = 0;
        foreach (var position in positions)
        {
            keys[index] = (long)position.Y << 32 | (uint)index;
            sorted[index++] = position;
        }

        Array.Sort(keys, sorted);
        return sorted;
    }

    private sealed class DecoratorView(TreeState tree) : TreeDecoratorContext
    {
        public override IWorldGenLevel Level => tree.Level;

        public override IRandomSource Random => tree.Random;

        public override WorldGenerationContext Generation => tree.Generation;

        // Sorted on first use: decorators only add decorations, so the logs, leaves and roots don't change meanwhile.
        public override IReadOnlyList<Vector> Logs => field ??= SortedByY(tree.Logs);

        public override IReadOnlyList<Vector> Leaves => field ??= SortedByY(tree.Foliage);

        public override IReadOnlyList<Vector> Roots => field ??= SortedByY(tree.Roots);

        public override void SetBlock(Vector position, IBlock block)
        {
            tree.Decorations.Add(position);
            tree.Level.SetBlock(position, block);
        }
    }
}
