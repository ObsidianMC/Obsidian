using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A nether wart patch on the ceiling with weeping vines hanging from it, like vanilla's WeepingVinesFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:weeping_vines")]
public sealed class WeepingVinesFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:weeping_vines";

    private static IBlock NetherWartBlock => field ??= BlocksRegistry.Get(Material.NetherWartBlock);

    private static IBlock WeepingVines => field ??= BlocksRegistry.Get(Material.WeepingVines);

    private static IBlock WeepingVinesPlant => field ??= BlocksRegistry.Get(Material.WeepingVinesPlant);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir || !IsRoof(level.GetBlock(origin + Vector.Up)))
            return false;

        PlaceRoofNetherWart(level, random, origin);
        PlaceRoofWeepingVines(level, random, origin);
        return true;
    }

    /// <summary>
    /// Vanilla <c>WeepingVinesFeature.placeWeepingVinesColumn</c>: grows up to <paramref name="length"/> blocks downward,
    /// ending in a head with an age in <c>[minAge, maxAge]</c>.
    /// </summary>
    internal static void PlaceWeepingVinesColumn(IWorldGenLevel level, IRandomSource random, Vector position, int length, int minAge, int maxAge)
    {
        for (var i = 0; i <= length; i++)
        {
            if (level.GetBlock(position).IsAir)
            {
                if (i == length || !level.GetBlock(position + Vector.Down).IsAir)
                {
                    level.SetBlock(position, WeepingVines.WithProperty("age", FeatureHelpers.NextInt(random, minAge, maxAge)));
                    break;
                }

                level.SetBlock(position, WeepingVinesPlant);
            }

            position += Vector.Down;
        }
    }

    private static void PlaceRoofNetherWart(IWorldGenLevel level, IRandomSource random, Vector origin)
    {
        level.SetBlock(origin, NetherWartBlock);

        for (var i = 0; i < 200; i++)
        {
            var position = origin + new Vector(random.NextInt(6) - random.NextInt(6), random.NextInt(2) - random.NextInt(5),
                random.NextInt(6) - random.NextInt(6));
            if (!level.GetBlock(position).IsAir)
                continue;

            var neighbors = 0;
            foreach (var face in FeatureHelpers.Directions)
            {
                if (IsRoof(level.GetBlock(position.Offset(face))))
                    neighbors++;

                if (neighbors > 1)
                    break;
            }

            if (neighbors == 1)
                level.SetBlock(position, NetherWartBlock);
        }
    }

    private static void PlaceRoofWeepingVines(IWorldGenLevel level, IRandomSource random, Vector origin)
    {
        for (var i = 0; i < 100; i++)
        {
            var position = origin + new Vector(random.NextInt(8) - random.NextInt(8), random.NextInt(2) - random.NextInt(7),
                random.NextInt(8) - random.NextInt(8));
            if (!level.GetBlock(position).IsAir || !IsRoof(level.GetBlock(position + Vector.Up)))
                continue;

            var length = FeatureHelpers.NextInt(random, 1, 8);
            if (random.NextInt(6) == 0)
                length *= 2;

            if (random.NextInt(5) == 0)
                length = 1;

            PlaceWeepingVinesColumn(level, random, position, length, 17, 25);
        }
    }

    private static bool IsRoof(IBlock block) => block.Material is Material.Netherrack or Material.NetherWartBlock;
}
