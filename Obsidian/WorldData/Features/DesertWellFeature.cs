namespace Obsidian.WorldData.Features;

/// <summary>
/// The sandstone desert well with two suspicious sand blocks (with the <c>archaeology/desert_well</c> loot table) under its
/// water, like vanilla's DesertWellFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:desert_well")]
public sealed class DesertWellFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:desert_well";

    private static IBlock Sand => field ??= BlocksRegistry.Get(Material.Sand);

    private static IBlock SandstoneSlab => field ??= BlocksRegistry.Get(Material.SandstoneSlab);

    private static IBlock Sandstone => field ??= BlocksRegistry.Get(Material.Sandstone);

    private static IBlock Water => field ??= BlocksRegistry.Get(Material.Water);

    private static IBlock SuspiciousSand => field ??= BlocksRegistry.Get(Material.SuspiciousSand);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        if (!level.EnsureCanWrite(context.Origin))
            return false;

        var origin = context.Origin + Vector.Up;
        while (level.GetBlock(origin).IsAir && origin.Y > level.MinY + 2)
            origin += Vector.Down;

        if (level.GetBlock(origin).Material != Material.Sand)
            return false;

        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                if (level.GetBlock(origin + new Vector(dx, -1, dz)).IsAir && level.GetBlock(origin + new Vector(dx, -2, dz)).IsAir)
                    return false;
            }
        }

        for (var dy = -2; dy <= 0; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dz = -2; dz <= 2; dz++)
                    level.SetBlock(origin + new Vector(dx, dy, dz), Sandstone);
            }
        }

        level.SetBlock(origin, Water);
        foreach (var face in FeatureHelpers.Horizontal)
            level.SetBlock(origin.Offset(face), Water);

        var below = origin + Vector.Down;
        level.SetBlock(below, Sand);
        foreach (var face in FeatureHelpers.Horizontal)
            level.SetBlock(below.Offset(face), Sand);

        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                if (dx == -2 || dx == 2 || dz == -2 || dz == 2)
                    level.SetBlock(origin + new Vector(dx, 1, dz), Sandstone);
            }
        }

        level.SetBlock(origin + new Vector(2, 1, 0), SandstoneSlab);
        level.SetBlock(origin + new Vector(-2, 1, 0), SandstoneSlab);
        level.SetBlock(origin + new Vector(0, 1, 2), SandstoneSlab);
        level.SetBlock(origin + new Vector(0, 1, -2), SandstoneSlab);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                level.SetBlock(origin + new Vector(dx, 4, dz), dx == 0 && dz == 0 ? Sandstone : SandstoneSlab);
        }

        for (var dy = 1; dy <= 3; dy++)
        {
            level.SetBlock(origin + new Vector(-1, dy, -1), Sandstone);
            level.SetBlock(origin + new Vector(-1, dy, 1), Sandstone);
            level.SetBlock(origin + new Vector(1, dy, -1), Sandstone);
            level.SetBlock(origin + new Vector(1, dy, 1), Sandstone);
        }

        // Center, east, south, west, north; one pick for each suspicious sand depth.
        ReadOnlySpan<Vector> candidates = [origin, origin + Vector.East, origin + Vector.South, origin + Vector.West, origin + Vector.North];
        var random = context.Random;
        PlaceSuspiciousSand(level, candidates[random.NextInt(candidates.Length)] + Vector.Down);
        PlaceSuspiciousSand(level, candidates[random.NextInt(candidates.Length)] + new Vector(0, -2, 0));
        return true;
    }

    private static void PlaceSuspiciousSand(IWorldGenLevel level, Vector position)
    {
        level.SetBlock(position, SuspiciousSand);
        FeatureHelpers.SetBrushableLootTable(level, position, "minecraft:archaeology/desert_well", FeatureHelpers.AsLong(position));
    }
}
