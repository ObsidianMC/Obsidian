using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A basalt pillar hanging from a ceiling down to the floor, with ragged sides and a splayed base, like vanilla's
/// BasaltPillarFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:basalt_pillar")]
public sealed class BasaltPillarFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:basalt_pillar";

    private static IBlock Basalt => field ??= BlocksRegistry.Get(Material.Basalt);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir || level.GetBlock(origin + Vector.Up).IsAir)
            return false;

        var position = origin;
        var north = true;
        var south = true;
        var west = true;
        var east = true;

        while (level.GetBlock(position).IsAir)
        {
            if (level.IsOutsideBuildHeight(position.Y))
                return true;

            level.SetBlock(position, Basalt);

            // Each side keeps hanging off only while every block above it was placed.
            north = north && PlaceHangOff(level, random, position + Vector.North);
            south = south && PlaceHangOff(level, random, position + Vector.South);
            west = west && PlaceHangOff(level, random, position + Vector.West);
            east = east && PlaceHangOff(level, random, position + Vector.East);
            position += Vector.Down;
        }

        position += Vector.Up;
        PlaceBaseHangOff(level, random, position + Vector.North);
        PlaceBaseHangOff(level, random, position + Vector.South);
        PlaceBaseHangOff(level, random, position + Vector.West);
        PlaceBaseHangOff(level, random, position + Vector.East);
        position += Vector.Down;

        for (var dx = -3; dx < 4; dx++)
        {
            for (var dz = -3; dz < 4; dz++)
            {
                var spread = Math.Abs(dx) * Math.Abs(dz);
                if (random.NextInt(10) >= 10 - spread)
                    continue;

                var target = position + new Vector(dx, 0, dz);
                var drop = 3;
                while (level.GetBlock(target + Vector.Down).IsAir)
                {
                    target += Vector.Down;
                    if (--drop <= 0)
                        break;
                }

                if (!level.GetBlock(target + Vector.Down).IsAir)
                    level.SetBlock(target, Basalt);
            }
        }

        return true;
    }

    private static void PlaceBaseHangOff(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        if (random.NextBoolean())
            level.SetBlock(position, Basalt);
    }

    private static bool PlaceHangOff(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        if (random.NextInt(10) == 0)
            return false;

        level.SetBlock(position, Basalt);
        return true;
    }
}
