using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features.Tree;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// A compiled 26.3 <c>minecraft:tree</c>: a trunk, foliage, optional roots and decorators, then the leaf
/// <c>distance</c> pass and the shape updates around the tree, in vanilla's order and random draws.
/// </summary>
internal sealed class TreeConfig
{
    public TreeConfig(FeatureRegistry registry, JsonElement json)
    {
        this.Tags = new TreeTags(registry);
        this.TrunkProvider = registry.Provider(json.GetProperty("trunk_provider"));
        this.FoliageProvider = registry.Provider(json.GetProperty("foliage_provider"));
        this.BelowTrunkProvider = registry.Provider(json.GetProperty("below_trunk_provider"));
        this.Trunk = TrunkPlacer.Read(registry, json.GetProperty("trunk_placer"));
        this.Foliage = FoliagePlacer.Read(registry, json.GetProperty("foliage_placer"));
        this.MinimumSize = FeatureSize.Read(registry, json.GetProperty("minimum_size"));
        this.IgnoreVines = json.TryGetProperty("ignore_vines", out var ignoreVines) && ignoreVines.GetBoolean();

        if (json.TryGetProperty("root_placer", out var rootPlacer))
        {
            this.Roots = RootPlacer.Read(registry, rootPlacer);
        }

        var decorators = json.TryGetProperty("decorators", out var decoratorList)
            ? decoratorList.EnumerateArray().Select(entry => TreeDecorator.Read(registry, entry)).ToArray()
            : [];
        this.Decorators = decorators;
    }

    public TreeTags Tags { get; }

    public StateProvider TrunkProvider { get; }

    public StateProvider FoliageProvider { get; }

    public StateProvider BelowTrunkProvider { get; }

    public TrunkPlacer Trunk { get; }

    public FoliagePlacer Foliage { get; }

    public RootPlacer? Roots { get; }

    public FeatureSize MinimumSize { get; }

    public TreeDecorator[] Decorators { get; }

    public bool IgnoreVines { get; }

    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var tree = new TreePlacement(this, context, random);

        try
        {
            return this.Place(tree, origin);
        }
        finally
        {
            tree.Release();
        }
    }

    private bool Place(TreePlacement tree, Vector origin)
    {
        if (!this.DoPlace(tree, origin))
        {
            return false;
        }

        if (tree.Logs.Count == 0 && tree.Foliage.Count == 0)
        {
            return false;
        }

        if (this.Decorators.Length > 0)
        {
            var decorations = TreeDecoration.ForTree(tree);

            foreach (var decorator in this.Decorators)
            {
                decorator.Place(decorations);
            }
        }

        var level = tree.Level;
        var bounds = TreeBounds.Encapsulating(tree.Roots, tree.Logs, tree.Foliage, tree.Decorations);
        var shape = LeafDistanceUpdater.UpdateLeaves(
            level, bounds, tree.Logs, tree.Decorations, tree.Roots, this.Tags.OptionalLeafDistance);

        ShapeUpdater.UpdateShapeAtEdge(level, shape, bounds.Min);
        return true;
    }

    private bool DoPlace(TreePlacement tree, Vector origin)
    {
        var level = tree.Level;
        var random = tree.Random;

        var height = this.Trunk.TreeHeight(random);
        var foliageHeight = this.Foliage.FoliageHeight(random, height);
        var trunkLength = height - foliageHeight;
        var foliageRadius = this.Foliage.FoliageRadius(random, trunkLength);
        var trunkOrigin = this.Roots is null ? origin : this.Roots.TrunkOrigin(origin, random);

        var minY = Math.Min(origin.Y, trunkOrigin.Y);
        var maxY = Math.Max(origin.Y, trunkOrigin.Y) + height + 1;

        if (minY < level.MinY + 1 || maxY > level.MinY + level.Height)
        {
            return false;
        }

        var freeHeight = this.MaxFreeTreeHeight(level, height, trunkOrigin);
        var minClippedHeight = this.MinimumSize.MinClippedHeight;
        var clipped = freeHeight < height;

        if (clipped && (minClippedHeight is null || freeHeight < minClippedHeight))
        {
            return false;
        }

        if (this.Roots is not null && !this.Roots.PlaceRoots(tree, origin, trunkOrigin))
        {
            return false;
        }

        var attachments = this.Trunk.PlaceTrunk(tree, freeHeight, trunkOrigin);

        foreach (var attachment in attachments)
        {
            this.Foliage.CreateFoliage(tree, freeHeight, attachment, foliageHeight, foliageRadius);
        }

        return true;
    }

    // How tall the tree can grow before the minimum size around its trunk meets something.
    private int MaxFreeTreeHeight(IWorldGenLevel level, int height, Vector trunkOrigin)
    {
        for (var y = 0; y <= height + 1; y++)
        {
            var radius = this.MinimumSize.SizeAtHeight(height, y);

            for (var x = -radius; x <= radius; x++)
            {
                for (var z = -radius; z <= radius; z++)
                {
                    var position = trunkOrigin + (x, y, z);
                    var free = this.Trunk.IsFree(this.Tags, level, position);
                    var blockedByVine = !this.IgnoreVines && TreeBlocks.IsVine(level, position);

                    if (!free || blockedByVine)
                    {
                        return y - 2;
                    }
                }
            }
        }

        return height;
    }
}

/// <summary>The 26.3 block tags tree parts test.</summary>
internal sealed class TreeTags(FeatureRegistry registry)
{
    public HashSet<int> Logs { get; } = registry.BlockTag("minecraft:logs");

    public HashSet<int> Leaves { get; } = registry.BlockTag("minecraft:leaves");

    public HashSet<int> ReplaceableByTrees { get; } = registry.BlockTag("minecraft:replaceable_by_trees");

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    public bool ValidTreePos(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);

        return block.IsAir || this.ReplaceableByTrees.Contains(block.RegistryId);
    }

    /// <summary>Vanilla <c>TreeFeature.isAirOrLeaves</c>.</summary>
    public bool IsAirOrLeaves(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);

        return block.IsAir || this.Leaves.Contains(block.RegistryId);
    }

    public bool IsLog(IBlock block) => this.Logs.Contains(block.RegistryId);

    /// <summary>
    /// Vanilla <c>LeavesBlock.getOptionalDistanceAt</c>: 0 for logs, the <c>distance</c> of leaves, otherwise none.
    /// </summary>
    public int? OptionalLeafDistance(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);

        if (this.IsLog(block))
        {
            return 0;
        }

        if (!this.Leaves.Contains(block.RegistryId))
        {
            return null;
        }

        var distance = block.GetProperty("distance");

        return distance is null ? null : int.Parse(distance);
    }
}

/// <summary>
/// One tree being placed: the level and random, and the positions of its roots, logs, foliage and decorations.
/// Like vanilla's setters, every position is recorded even when the region rejects the write; the sets' vanilla
/// iteration order later drives the decorators and the leaf distance pass.
/// </summary>
internal sealed class TreePlacement(TreeConfig config, DecorationContext context, IRandomSource random)
{
    public TreeConfig Config { get; } = config;

    public DecorationContext Context { get; } = context;

    public IWorldGenLevel Level => this.Context.Level;

    public IRandomSource Random { get; } = random;

    public TreeTags Tags => this.Config.Tags;

    public VanillaBlockPosSet Roots { get; } = new();

    public VanillaBlockPosSet Logs { get; } = new();

    public VanillaBlockPosSet Foliage { get; } = new();

    public VanillaBlockPosSet Decorations { get; } = new();

    /// <summary>The tree as plugins' tree parts see it, made for the first that asks.</summary>
    public PluginTreeContext PluginContext => field ??= new PluginTreeContext(this);

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

    public void SetFoliage(Vector position, IBlock block)
    {
        this.Foliage.Add(position);
        this.Level.SetBlock(position, block);
    }

    public bool IsFoliageSet(Vector position) => this.Foliage.Contains(position);

    public void SetDecoration(Vector position, IBlock block)
    {
        this.Decorations.Add(position);
        this.Level.SetBlock(position, block);
    }

    public IBlock TrunkState(Vector position) =>
        FeatureRegistry.State(this.Config.TrunkProvider, this.Level, this.Random, position);

    public IBlock FoliageState(Vector position) =>
        FeatureRegistry.State(this.Config.FoliageProvider, this.Level, this.Random, position);

    /// <summary>Vanilla <c>TrunkPlacer.placeBelowTrunkBlock</c>: the provider's optional state, as a log.</summary>
    public void PlaceBelowTrunk(Vector position)
    {
        var state = this.Config.BelowTrunkProvider(this.Level, this.Random, position);

        if (state is not null)
        {
            this.SetLog(position, state);
        }
    }

    /// <summary>Gives the position sets' storage back to the thread's pool once the tree is done.</summary>
    public void Release()
    {
        this.Roots.Release();
        this.Logs.Release();
        this.Foliage.Release();
        this.Decorations.Release();
    }
}
