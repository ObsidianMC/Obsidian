using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Shared logic of vanilla's AbstractHugeMushroomFeature: checks the space, then builds the cap and a stem
/// (4-6 tall, doubled with 1 in 12 chance).
/// </summary>
public abstract class HugeMushroomFeatureBase : ConfiguredFeatureBase
{
    private static readonly BlockSet mushroomGrowBlock = new("#minecraft:mushroom_grow_block");
    private static readonly BlockSet leaves = new("#minecraft:leaves");
    private static readonly BlockSet replaceableByMushrooms = new("#minecraft:replaceable_by_mushrooms");

    public required IBlockStateProvider CapProvider { get; init; }

    public required IBlockStateProvider StemProvider { get; init; }

    public int FoliageRadius { get; init; } = 2;

    public sealed override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var height = random.NextInt(3) + 4;
        if (random.NextInt(12) == 0)
            height *= 2;

        if (!this.IsValidPosition(level, origin, height))
            return false;

        this.MakeCap(level, random, origin, height);

        for (var i = 0; i < height; i++)
            PlaceMushroomBlock(level, origin + new Vector(0, i, 0), this.StemProvider.GetState(random, origin));

        return true;
    }

    /// <summary>
    /// Radius that must be clear at <paramref name="y"/> above the origin. Vanilla always calls it with -1 for the height.
    /// </summary>
    protected abstract int GetTreeRadiusForHeight(int unused, int height, int foliageRadius, int y);

    protected abstract void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height);

    /// <summary>
    /// Places a mushroom block where there's air or a block mushrooms may replace.
    /// </summary>
    protected static void PlaceMushroomBlock(IWorldGenLevel level, Vector position, IBlock state)
    {
        var existing = level.GetBlock(position);
        if (existing.IsAir || replaceableByMushrooms.Contains(existing))
            level.SetBlock(position, state);
    }

    private bool IsValidPosition(IWorldGenLevel level, Vector origin, int height)
    {
        if (origin.Y < level.MinY + 1 || origin.Y + height + 1 > level.MinY + level.Height - 1)
            return false;

        var below = level.GetBlock(origin + Vector.Down);
        if (!FeatureHelpers.IsDirt(below) && !mushroomGrowBlock.Contains(below))
            return false;

        for (var y = 0; y <= height; y++)
        {
            var radius = this.GetTreeRadiusForHeight(-1, -1, this.FoliageRadius, y);
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var state = level.GetBlock(origin + new Vector(dx, y, dz));
                    if (!state.IsAir && !leaves.Contains(state))
                        return false;
                }
            }
        }

        return true;
    }
}
