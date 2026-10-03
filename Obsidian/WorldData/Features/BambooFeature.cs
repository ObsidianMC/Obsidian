namespace Obsidian.WorldData.Features;

/// <summary>
/// A bamboo stalk with leafy top, optionally turning nearby dirt into podzol, like vanilla's BambooFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:bamboo")]
public sealed class BambooFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:bamboo";

    /// <summary>
    /// Chance of a podzol disk around the stalk.
    /// </summary>
    public required float Probability { get; init; }

    private static IBlock Bamboo => field ??= BlocksRegistry.Get(Material.Bamboo);

    private static IBlock Podzol => field ??= BlocksRegistry.Get(Material.Podzol);

    private static IBlock Trunk => field ??= Bamboo.WithProperty("age", 1).WithProperty("leaves", "none").WithProperty("stage", 0);

    private static IBlock FinalLarge => field ??= Trunk.WithProperty("leaves", "large").WithProperty("stage", 1);

    private static IBlock TopLarge => field ??= Trunk.WithProperty("leaves", "large");

    private static IBlock TopSmall => field ??= Trunk.WithProperty("leaves", "small");

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir)
            return false;

        if (Bamboo.CanSurvive(level, origin))
        {
            var height = random.NextInt(12) + 5;
            if (random.NextFloat() < this.Probability)
            {
                var radius = random.NextInt(4) + 1;
                for (var x = origin.X - radius; x <= origin.X + radius; x++)
                {
                    for (var z = origin.Z - radius; z <= origin.Z + radius; z++)
                    {
                        var dx = x - origin.X;
                        var dz = z - origin.Z;
                        if (dx * dx + dz * dz > radius * radius)
                            continue;

                        var ground = new Vector(x, level.GetHeight(HeightmapType.WorldSurface, x, z) - 1, z);
                        if (FeatureHelpers.IsDirt(level.GetBlock(ground)))
                            level.SetBlock(ground, Podzol);
                    }
                }
            }

            var position = origin;
            for (var i = 0; i < height && level.GetBlock(position).IsAir; i++)
            {
                level.SetBlock(position, Trunk);
                position += Vector.Up;
            }

            if (position.Y - origin.Y >= 3)
            {
                level.SetBlock(position, FinalLarge);
                level.SetBlock(position + Vector.Down, TopLarge);
                level.SetBlock(position + new Vector(0, -2, 0), TopSmall);
            }
        }

        return true;
    }
}
