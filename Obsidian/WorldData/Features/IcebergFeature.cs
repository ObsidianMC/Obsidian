using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Floating icebergs at sea level (round or tilted ellipse shapes, optional snow caps and a carved channel), like vanilla's
/// IcebergFeature.
/// </summary>
/// <remarks>
/// The float/double mix and every random draw follow vanilla exactly; <c>Math.pow(v, 2)</c> is computed as <c>v * v</c>
/// like HotSpot does.
/// </remarks>
[ConfiguredFeatureClass("minecraft:iceberg")]
public sealed class IcebergFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:iceberg";

    /// <summary>
    /// Main iceberg block (packed ice or blue ice).
    /// </summary>
    public required SimpleBlockState State { get; init; }

    private IBlock Block => field ??= BlocksRegistry.GetFromSimpleState(this.State);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    private static IBlock Water => field ??= BlocksRegistry.Get(Material.Water);

    private static IBlock SnowBlock => field ??= BlocksRegistry.Get(Material.SnowBlock);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        if (!level.EnsureCanWrite(context.Origin))
            return false;

        var origin = context.Origin.AtY(level.SeaLevel);
        var snowOnTop = random.NextDouble() > 0.7;
        var state = this.Block;
        var angle = random.NextDouble() * 2.0 * Math.PI;
        var ellipseA = 11 - random.NextInt(5);
        var ellipseC = 3 + random.NextInt(3);
        var ellipse = random.NextDouble() > 0.7;
        var height = ellipse ? random.NextInt(6) + 6 : random.NextInt(15) + 3;
        if (!ellipse && random.NextDouble() > 0.9)
            height += random.NextInt(19) + 7;

        var depth = Math.Min(height + random.NextInt(11), 18);
        var width = Math.Min(height + random.NextInt(7) - random.NextInt(5), 11);
        var extent = ellipse ? ellipseA : 11;

        for (var x = -extent; x < extent; x++)
        {
            for (var z = -extent; z < extent; z++)
            {
                for (var y = 0; y < height; y++)
                {
                    var radius = ellipse ? RadiusEllipse(y, height, width) : RadiusRound(random, y, height, width);
                    if (ellipse || x < radius)
                        this.GenerateIcebergBlock(level, random, origin, height, x, y, z, radius, extent, ellipse, ellipseC, angle, snowOnTop, state);
                }
            }
        }

        Smooth(level, origin, width, height, ellipse, ellipseA);

        for (var x = -extent; x < extent; x++)
        {
            for (var z = -extent; z < extent; z++)
            {
                for (var y = -1; y > -depth; y--)
                {
                    var a = ellipse ? FeatureHelpers.Ceil(extent * (1.0f - (float)(y * y) / (depth * 8.0f))) : extent;
                    var radius = RadiusSteep(random, -y, depth, width);
                    if (x < radius)
                        this.GenerateIcebergBlock(level, random, origin, depth, x, y, z, radius, a, ellipse, ellipseC, angle, snowOnTop, state);
                }
            }
        }

        var cutOut = ellipse ? random.NextDouble() > 0.1 : random.NextDouble() > 0.7;
        if (cutOut)
            GenerateCutOut(random, level, width, height, origin, ellipse, ellipseA, angle, ellipseC);

        return true;
    }

    private static void GenerateCutOut(IRandomSource random, IWorldGenLevel level, int width, int height, Vector origin, bool ellipse,
        int ellipseA, double angle, int ellipseC)
    {
        var signX = random.NextBoolean() ? -1 : 1;
        var signZ = random.NextBoolean() ? -1 : 1;
        var offsetX = random.NextInt(Math.Max(width / 2 - 2, 1));
        if (random.NextBoolean())
            offsetX = width / 2 + 1 - random.NextInt(Math.Max(width - width / 2 - 1, 1));

        var offsetZ = random.NextInt(Math.Max(width / 2 - 2, 1));
        if (random.NextBoolean())
            offsetZ = width / 2 + 1 - random.NextInt(Math.Max(width - width / 2 - 1, 1));

        if (ellipse)
            offsetX = offsetZ = random.NextInt(Math.Max(ellipseA - 5, 1));

        var center = new Vector(signX * offsetX, 0, signZ * offsetZ);
        var cutAngle = ellipse ? angle + Math.PI / 2 : random.NextDouble() * 2.0 * Math.PI;

        for (var y = 0; y < height - 3; y++)
        {
            var radius = RadiusRound(random, y, height, width);
            Carve(radius, y, origin, level, false, cutAngle, center, ellipseA, ellipseC);
        }

        // The loop bound draws a new nextInt(5) on every check, like vanilla.
        for (var y = -1; y > -height + random.NextInt(5); y--)
        {
            var radius = RadiusSteep(random, -y, height, width);
            Carve(radius, y, origin, level, true, cutAngle, center, ellipseA, ellipseC);
        }
    }

    private static void Carve(int radius, int y, Vector origin, IWorldGenLevel level, bool underwater, double angle, Vector center,
        int ellipseA, int ellipseC)
    {
        var extent = radius + 1 + ellipseA / 3;
        var c = Math.Min(radius - 3, 3) + ellipseC / 2 - 1;

        for (var x = -extent; x < extent; x++)
        {
            for (var z = -extent; z < extent; z++)
            {
                if (SignedDistanceEllipse(x, z, center, extent, c, angle) >= 0.0)
                    continue;

                var position = origin + new Vector(x, y, z);
                var existing = level.GetBlock(position);
                if (!IsIcebergState(existing) && existing.Material != Material.SnowBlock)
                    continue;

                if (underwater)
                {
                    level.SetBlock(position, Water);
                }
                else
                {
                    level.SetBlock(position, Air);

                    if (level.GetBlock(position + Vector.Up).Material == Material.Snow)
                        level.SetBlock(position + Vector.Up, Air);
                }
            }
        }
    }

    private void GenerateIcebergBlock(IWorldGenLevel level, IRandomSource random, Vector origin, int height, int x, int y, int z, int radius,
        int a, bool ellipse, int ellipseC, double angle, bool snowOnTop, IBlock state)
    {
        var distance = ellipse
            ? SignedDistanceEllipse(x, z, Vector.Zero, a, GetEllipseC(y, height, ellipseC), angle)
            : SignedDistanceCircle(x, z, Vector.Zero, radius, random);

        if (distance >= 0.0)
            return;

        double threshold = ellipse ? -0.5 : -6 - random.NextInt(3);
        if (distance > threshold && random.NextDouble() > 0.9)
            return;

        SetIcebergBlock(origin + new Vector(x, y, z), level, random, height - y, height, ellipse, snowOnTop, state);
    }

    private static void SetIcebergBlock(Vector position, IWorldGenLevel level, IRandomSource random, int heightAbove, int height, bool ellipse,
        bool snowOnTop, IBlock state)
    {
        var existing = level.GetBlock(position);
        if (!existing.IsAir && existing.Material is not (Material.SnowBlock or Material.Ice or Material.Water))
            return;

        var snowAllowed = !ellipse || random.NextDouble() > 0.05;
        var divisor = ellipse ? 3 : 2;
        if (snowOnTop && existing.Material != Material.Water
            && heightAbove <= random.NextInt(Math.Max(1, height / divisor)) + height * 0.6 && snowAllowed)
        {
            level.SetBlock(position, SnowBlock);
        }
        else
        {
            level.SetBlock(position, state);
        }
    }

    private static int GetEllipseC(int y, int height, int ellipseC)
    {
        var c = ellipseC;
        if (y > 0 && height - y <= 3)
            c -= 4 - (height - y);

        return c;
    }

    private static double SignedDistanceCircle(int x, int z, Vector center, int radius, IRandomSource random)
    {
        var offset = 10.0f * Math.Clamp(random.NextFloat(), 0.2f, 0.8f) / radius;
        double dx = x - center.X;
        double dz = z - center.Z;
        return offset + dx * dx + dz * dz - (double)radius * radius;
    }

    private static double SignedDistanceEllipse(int x, int z, Vector center, int a, int c, double angle)
    {
        double dx = x - center.X;
        double dz = z - center.Z;
        var u = (dx * Math.Cos(angle) - dz * Math.Sin(angle)) / a;
        var v = (dx * Math.Sin(angle) + dz * Math.Cos(angle)) / c;
        return u * u + v * v - 1.0;
    }

    private static int RadiusRound(IRandomSource random, int y, int height, int width)
    {
        var scale = 3.5f - random.NextFloat();
        var radius = (1.0f - (float)(y * y) / (height * scale)) * width;
        if (height > 15 + random.NextInt(5))
        {
            var level = y < 3 + random.NextInt(6) ? y / 2 : y;
            radius = (1.0f - level / (height * scale * 0.4f)) * width;
        }

        return FeatureHelpers.Ceil(radius / 2.0f);
    }

    private static int RadiusEllipse(int y, int height, int width)
    {
        var radius = (1.0f - (float)(y * y) / (height * 1.0f)) * width;
        return FeatureHelpers.Ceil(radius / 2.0f);
    }

    private static int RadiusSteep(IRandomSource random, int y, int height, int width)
    {
        var scale = 1.0f + random.NextFloat() / 2.0f;
        var radius = (1.0f - y / (height * scale)) * width;
        return FeatureHelpers.Ceil(radius / 2.0f);
    }

    private static bool IsIcebergState(IBlock block) => block.Material is Material.PackedIce or Material.SnowBlock or Material.BlueIce;

    private static void Smooth(IWorldGenLevel level, Vector origin, int width, int height, bool ellipse, int ellipseA)
    {
        var range = ellipse ? ellipseA : width / 2;

        for (var x = -range; x <= range; x++)
        {
            for (var z = -range; z <= range; z++)
            {
                for (var y = 0; y <= height; y++)
                {
                    var position = origin + new Vector(x, y, z);
                    var existing = level.GetBlock(position);
                    if (!IsIcebergState(existing) && existing.Material != Material.Snow)
                        continue;

                    if (level.GetBlock(position + Vector.Down).IsAir)
                    {
                        level.SetBlock(position, Air);
                        level.SetBlock(position + Vector.Up, Air);
                    }
                    else if (IsIcebergState(existing))
                    {
                        var open = 0;
                        foreach (var offset in (ReadOnlySpan<Vector>)[Vector.West, Vector.East, Vector.North, Vector.South])
                        {
                            if (!IsIcebergState(level.GetBlock(position + offset)))
                                open++;
                        }

                        if (open >= 3)
                            level.SetBlock(position, Air);
                    }
                }
            }
        }
    }
}
