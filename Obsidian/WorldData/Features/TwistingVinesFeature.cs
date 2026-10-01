using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Twisting vine columns growing up from netherrack and warped blocks, like vanilla's TwistingVinesFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:twisting_vines")]
public sealed class TwistingVinesFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:twisting_vines";

    /// <summary>
    /// Horizontal spread; <c>SpreadWidth²</c> columns are attempted.
    /// </summary>
    public required int SpreadWidth { get; init; }

    public required int SpreadHeight { get; init; }

    public required int MaxHeight { get; init; }

    private static IBlock TwistingVines => field ??= BlocksRegistry.Get(Material.TwistingVines);

    private static IBlock TwistingVinesPlant => field ??= BlocksRegistry.Get(Material.TwistingVinesPlant);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || IsInvalidPlacementLocation(level, origin))
            return false;

        var width = this.SpreadWidth;
        var heightSpread = this.SpreadHeight;

        for (var i = 0; i < width * width; i++)
        {
            var dx = FeatureHelpers.NextInt(random, -width, width);
            var dy = FeatureHelpers.NextInt(random, -heightSpread, heightSpread);
            var dz = FeatureHelpers.NextInt(random, -width, width);
            var position = FindFirstAirBlockAboveGround(level, origin + new Vector(dx, dy, dz));
            if (position is null || IsInvalidPlacementLocation(level, position.Value))
                continue;

            var length = FeatureHelpers.NextInt(random, 1, this.MaxHeight);
            if (random.NextInt(6) == 0)
                length *= 2;

            if (random.NextInt(5) == 0)
                length = 1;

            PlaceTwistingVinesColumn(level, random, position.Value, length, 17, 25);
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>TwistingVinesFeature.placeWeepingVinesColumn</c> (sic): grows up to <paramref name="length"/> blocks upward,
    /// ending in a head with an age in <c>[minAge, maxAge]</c>.
    /// </summary>
    internal static void PlaceTwistingVinesColumn(IWorldGenLevel level, IRandomSource random, Vector position, int length, int minAge, int maxAge)
    {
        for (var i = 1; i <= length; i++)
        {
            if (level.GetBlock(position).IsAir)
            {
                if (i == length || !level.GetBlock(position + Vector.Up).IsAir)
                {
                    level.SetBlock(position, TwistingVines.WithProperty("age", FeatureHelpers.NextInt(random, minAge, maxAge)));
                    break;
                }

                level.SetBlock(position, TwistingVinesPlant);
            }

            position += Vector.Up;
        }
    }

    // Moves down to the first air block above ground; null when it leaves the world.
    private static Vector? FindFirstAirBlockAboveGround(IWorldGenLevel level, Vector position)
    {
        do
        {
            position += Vector.Down;
            if (level.IsOutsideBuildHeight(position.Y))
                return null;
        }
        while (level.GetBlock(position).IsAir);

        return position + Vector.Up;
    }

    private static bool IsInvalidPlacementLocation(IWorldGenLevel level, Vector position)
    {
        if (!level.GetBlock(position).IsAir)
            return true;

        var below = level.GetBlock(position + Vector.Down);
        return below.Material is not (Material.Netherrack or Material.WarpedNylium or Material.WarpedWartBlock);
    }
}
