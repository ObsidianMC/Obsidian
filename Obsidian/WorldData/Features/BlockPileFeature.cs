using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A low, ragged heap of blocks (hay, melons, pumpkins, ice in villages and the like), like vanilla's BlockPileFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:block_pile")]
public sealed class BlockPileFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:block_pile";

    public required IBlockStateProvider StateProvider { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || origin.Y < level.MinY + 5)
            return false;

        var radiusX = 2 + random.NextInt(2);
        var radiusZ = 2 + random.NextInt(2);

        foreach (var position in FeatureHelpers.BetweenClosed(origin + new Vector(-radiusX, 0, -radiusZ), origin + new Vector(radiusX, 1, radiusZ)))
        {
            var dx = origin.X - position.X;
            var dz = origin.Z - position.Z;

            // Both floats are drawn every iteration; the second roll only happens when the first check fails.
            if (dx * dx + dz * dz <= random.NextFloat() * 10.0f - random.NextFloat() * 6.0f)
                this.TryPlaceBlock(level, position, random);
            else if (random.NextFloat() < 0.031)
                this.TryPlaceBlock(level, position, random);
        }

        return true;
    }

    private void TryPlaceBlock(IWorldGenLevel level, Vector position, IRandomSource random)
    {
        if (level.GetBlock(position).IsAir && MayPlaceOn(level, position, random))
            level.SetBlock(position, this.StateProvider.GetState(random, position));
    }

    private static bool MayPlaceOn(IWorldGenLevel level, Vector position, IRandomSource random)
    {
        var below = level.GetBlock(position + Vector.Down);
        return below.Material == Material.DirtPath ? random.NextBoolean() : below.IsFaceSturdy(BlockFace.Up);
    }
}
