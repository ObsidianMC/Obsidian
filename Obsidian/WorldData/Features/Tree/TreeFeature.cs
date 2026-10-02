namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Vanilla <c>minecraft:tree</c>: a trunk, foliage, optional roots and decorators, followed by vanilla's leaf
/// <c>distance</c> pass and neighbor shape updates around the tree.
/// </summary>
[ConfiguredFeatureClass("minecraft:tree")]
public sealed class TreeFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:tree";

    public required IBlockStateProvider TrunkProvider { get; init; }

    public required TrunkPlacer TrunkPlacer { get; init; }

    public required IBlockStateProvider FoliageProvider { get; init; }

    public required FoliagePlacer FoliagePlacer { get; init; }

    public RootPlacer? RootPlacer { get; init; }

    /// <summary>Placed under the trunk (see <see cref="ForceDirt"/>).</summary>
    public required IBlockStateProvider DirtProvider { get; init; }

    public required FeatureSize MinimumSize { get; init; }

    public TreeDecorator[] Decorators { get; init; } = [];

    /// <summary>When <c>false</c>, existing vines in the tree's space block placement.</summary>
    public bool IgnoreVines { get; init; }

    /// <summary>Replace the block under the trunk with <see cref="DirtProvider"/> even if it's already dirt.</summary>
    public bool ForceDirt { get; init; }

    public override bool Place(FeatureContext context)
    {
        if (!context.Level.EnsureCanWrite(context.Origin))
            return false;

        var tree = new TreeContext(this, context.Level, context.Random, context.Generation);
        try
        {
            return this.Place(tree, context.Origin);
        }
        finally
        {
            tree.Release();
        }
    }

    private bool Place(TreeContext tree, Vector origin)
    {
        if (!this.DoPlace(tree, origin) || (tree.Logs.Count == 0 && tree.Foliage.Count == 0))
            return false;

        if (this.Decorators.Length > 0)
        {
            var decoratorContext = new TreeDecoratorContext(tree);
            foreach (var decorator in this.Decorators)
                decorator.Place(decoratorContext);
        }

        var bounds = TreeBounds.Encapsulating(tree.Roots, tree.Logs, tree.Foliage, tree.Decorations);
        var shape = LeafDistanceUpdater.UpdateLeaves(tree.Level, bounds, tree.Logs, tree.Decorations, tree.Roots);
        ShapeUpdater.UpdateShapeAtEdge(tree.Level, shape, bounds.Min);
        return true;
    }

    private bool DoPlace(TreeContext tree, Vector origin)
    {
        var level = tree.Level;
        var random = tree.Random;

        var height = this.TrunkPlacer.GetTreeHeight(random);
        var foliageHeight = this.FoliagePlacer.GetFoliageHeight(random, height);
        var trunkLength = height - foliageHeight;
        var foliageRadius = this.FoliagePlacer.GetFoliageRadius(random, trunkLength);
        var trunkOrigin = this.RootPlacer is null ? origin : this.RootPlacer.GetTrunkOrigin(origin, random);

        var minY = Math.Min(origin.Y, trunkOrigin.Y);
        var maxY = Math.Max(origin.Y, trunkOrigin.Y) + height + 1;
        if (minY < level.MinY + 1 || maxY > level.MinY + level.Height)
            return false;

        var freeHeight = this.GetMaxFreeTreeHeight(level, height, trunkOrigin);
        var minClippedHeight = this.MinimumSize.MinClippedHeight;
        if (freeHeight < height && (minClippedHeight == FeatureSize.NoMinClippedHeight || freeHeight < minClippedHeight))
            return false;

        if (this.RootPlacer is not null && !this.RootPlacer.PlaceRoots(tree, origin, trunkOrigin))
            return false;

        foreach (var attachment in this.TrunkPlacer.PlaceTrunk(tree, freeHeight, trunkOrigin))
            this.FoliagePlacer.CreateFoliage(tree, freeHeight, attachment, foliageHeight, foliageRadius);

        return true;
    }

    /// <summary>How tall the tree can grow before hitting something within <see cref="MinimumSize"/>.</summary>
    private int GetMaxFreeTreeHeight(IWorldGenLevel level, int height, Vector trunkOrigin)
    {
        for (var y = 0; y <= height + 1; y++)
        {
            var radius = this.MinimumSize.GetSizeAtHeight(height, y);
            for (var x = -radius; x <= radius; x++)
            {
                for (var z = -radius; z <= radius; z++)
                {
                    var position = trunkOrigin + (x, y, z);
                    if (!this.TrunkPlacer.IsFree(level, position) || (!this.IgnoreVines && TreeBlocks.IsVine(level, position)))
                        return y - 2;
                }
            }
        }

        return height;
    }
}
