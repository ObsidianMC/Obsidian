namespace Obsidian.WorldData.Features;

/// <summary>
/// Clusters of basalt columns rising from the floor or the lava sea (basalt deltas), like vanilla's BasaltColumnsFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:basalt_columns")]
public sealed class BasaltColumnsFeature : ConfiguredFeatureBase
{
    private const int ClusteredReach = 5;
    private const int ClusteredSize = 50;
    private const int UnclusteredReach = 8;
    private const int UnclusteredSize = 15;

    public override string Type => "minecraft:basalt_columns";

    /// <summary>
    /// Height of the tallest column; columns further from the origin are shorter.
    /// </summary>
    public required IIntProvider Height { get; init; }

    /// <summary>
    /// Horizontal reach of each column.
    /// </summary>
    public required IIntProvider Reach { get; init; }

    private static IBlock Basalt => field ??= BlocksRegistry.Get(Material.Basalt);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        var seaLevel = level.SeaLevel;
        if (!level.EnsureCanWrite(origin) || !CanPlaceAt(level, seaLevel, origin))
            return false;

        var height = this.Height.Sample(random);
        var clustered = random.NextFloat() < 0.9f;
        var reach = Math.Min(height, clustered ? ClusteredReach : UnclusteredReach);
        var count = clustered ? ClusteredSize : UnclusteredSize;
        var placed = false;

        foreach (var position in FeatureHelpers.RandomBetweenClosed(random, count, origin + new Vector(-reach, 0, -reach),
            origin + new Vector(reach, 0, reach)))
        {
            var columnHeight = height - FeatureHelpers.DistManhattan(position, origin);
            if (columnHeight >= 0)
                placed |= PlaceColumn(level, seaLevel, position, columnHeight, this.Reach.Sample(random));
        }

        return placed;
    }

    private static bool PlaceColumn(IWorldGenLevel level, int seaLevel, Vector center, int height, int reach)
    {
        var placed = false;

        foreach (var position in FeatureHelpers.BetweenClosed(center + new Vector(-reach, 0, -reach), center + new Vector(reach, 0, reach)))
        {
            var distance = FeatureHelpers.DistManhattan(position, center);
            var start = IsAirOrLavaOcean(level, seaLevel, position)
                ? FindSurface(level, seaLevel, position, distance)
                : FindAir(level, position, distance);
            if (start is null)
                continue;

            var remaining = height - distance / 2;
            var cursor = start.Value;
            while (remaining >= 0)
            {
                if (IsAirOrLavaOcean(level, seaLevel, cursor))
                {
                    level.SetBlock(cursor, Basalt);
                    cursor += Vector.Up;
                    placed = true;
                }
                else
                {
                    if (level.GetBlock(cursor).Material != Material.Basalt)
                        break;

                    cursor += Vector.Up;
                }

                remaining--;
            }
        }

        return placed;
    }

    private static Vector? FindSurface(IWorldGenLevel level, int seaLevel, Vector position, int distance)
    {
        while (position.Y > level.MinY + 1 && distance > 0)
        {
            distance--;
            if (CanPlaceAt(level, seaLevel, position))
                return position;

            position += Vector.Down;
        }

        return null;
    }

    private static Vector? FindAir(IWorldGenLevel level, Vector position, int distance)
    {
        while (position.Y <= level.MinY + level.Height - 1 && distance > 0)
        {
            distance--;
            var state = level.GetBlock(position);
            if (CannotPlaceOn(state))
                return null;

            if (state.IsAir)
                return position;

            position += Vector.Up;
        }

        return null;
    }

    private static bool CanPlaceAt(IWorldGenLevel level, int seaLevel, Vector position)
    {
        if (!IsAirOrLavaOcean(level, seaLevel, position))
            return false;

        var below = level.GetBlock(position + Vector.Down);
        return !below.IsAir && !CannotPlaceOn(below);
    }

    private static bool IsAirOrLavaOcean(IWorldGenLevel level, int seaLevel, Vector position)
    {
        var state = level.GetBlock(position);
        return state.IsAir || state.Material == Material.Lava && position.Y <= seaLevel;
    }

    private static bool CannotPlaceOn(IBlock block) => block.Material is Material.Lava or Material.Bedrock or Material.MagmaBlock
        or Material.SoulSand or Material.NetherBricks or Material.NetherBrickFence or Material.NetherBrickStairs or Material.NetherWart
        or Material.Chest or Material.Spawner;
}
