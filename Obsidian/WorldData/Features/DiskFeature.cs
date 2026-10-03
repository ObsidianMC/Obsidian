using Obsidian.Providers.BlockStateProviders;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A flat disk of sand, clay or gravel replacing <see cref="Target"/> blocks around the origin, like vanilla's DiskFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:disk")]
public sealed class DiskFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:disk";

    public required RuleBasedStateProvider StateProvider { get; init; }

    /// <summary>
    /// Blocks the disk may replace.
    /// </summary>
    public required IBlockPredicate Target { get; init; }

    public required IIntProvider Radius { get; init; }

    /// <summary>
    /// The disk spans from <c>HalfHeight</c> above the origin to <c>HalfHeight</c> below it.
    /// </summary>
    public required int HalfHeight { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var top = origin.Y + this.HalfHeight;
        var bottom = origin.Y - this.HalfHeight - 1;
        var radius = this.Radius.Sample(random);
        var placed = false;

        // BlockPos.betweenClosed order: x fastest, then z.
        for (var z = origin.Z - radius; z <= origin.Z + radius; z++)
        {
            for (var x = origin.X - radius; x <= origin.X + radius; x++)
            {
                var dx = x - origin.X;
                var dz = z - origin.Z;
                if (dx * dx + dz * dz <= radius * radius)
                    placed |= this.PlaceColumn(level, context, top, bottom, x, z);
            }
        }

        return placed;
    }

    private bool PlaceColumn(IWorldGenLevel level, FeatureContext context, int top, int bottom, int x, int z)
    {
        var placed = false;
        for (var y = top; y > bottom; y--)
        {
            var position = new Vector(x, y, z);
            if (!this.Target.Test(level, position))
                continue;

            level.SetBlock(position, this.StateProvider.GetState(level, context.Random, position));
            placed = true;
        }

        return placed;
    }
}
