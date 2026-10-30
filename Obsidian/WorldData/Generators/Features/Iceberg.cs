using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature Iceberg(JsonElement json) =>
        SharedAlgorithm(new IcebergShape(ReadBlock(json.GetProperty("state"))));
}

/// <summary>26.3 iceberg geometry, isolated from the live generator's legacy implementation.</summary>
internal sealed class IcebergShape(IBlock state) : ConfiguredFeatureBase
{
    public override string Type => "minecraft:iceberg";

    private static IBlock Air => field ??= BlocksRegistry.Air;

    private static IBlock Water => field ??= BlocksRegistry.Water;

    private static IBlock SnowBlock => field ??= BlocksRegistry.SnowBlock;

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin.AtY(level.SeaLevel);
        var snowOnTop = random.NextDouble() > 0.7;
        var iceberg = state;
        var angle = random.NextDouble() * 2.0 * Math.PI;
        var ellipseA = 11 - random.NextInt(5);
        var ellipseC = 3 + random.NextInt(3);
        var ellipse = random.NextDouble() > 0.7;
        var height = ellipse ? random.NextInt(6) + 6 : random.NextInt(15) + 3;
        if (!ellipse && random.NextDouble() > 0.9)
        {
            height += random.NextInt(19) + 7;
        }

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
                    {
                        GenerateIcebergBlock(
                            level, random, origin, height, x, y, z, radius, extent,
                            ellipse, ellipseC, angle, snowOnTop, iceberg);
                    }
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
                    {
                        GenerateIcebergBlock(
                            level, random, origin, depth, x, y, z, radius, a,
                            ellipse, ellipseC, angle, snowOnTop, iceberg);
                    }
                }
            }
        }

        var cutOut = ellipse ? random.NextDouble() > 0.1 : random.NextDouble() > 0.7;
        if (cutOut)
        {
            GenerateCutOut(random, level, width, height, origin, ellipse, ellipseA, angle, ellipseC);
        }

        return true;
    }

    private static void GenerateCutOut(
        IRandomSource random, IWorldGenLevel level, int width, int height, Vector origin,
        bool ellipse, int ellipseA, double angle, int ellipseC)
    {
        var signX = random.NextBoolean() ? -1 : 1;
        var signZ = random.NextBoolean() ? -1 : 1;
        var offsetX = random.NextInt(Math.Max(width / 2 - 2, 1));
        if (random.NextBoolean())
        {
            offsetX = width / 2 + 1 - random.NextInt(Math.Max(width - width / 2 - 1, 1));
        }

        var offsetZ = random.NextInt(Math.Max(width / 2 - 2, 1));
        if (random.NextBoolean())
        {
            offsetZ = width / 2 + 1 - random.NextInt(Math.Max(width - width / 2 - 1, 1));
        }

        if (ellipse)
        {
            offsetZ = random.NextInt(Math.Max(ellipseA - 5, 1));
            offsetX = offsetZ;
        }

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

    private static void Carve(
        int radius, int y, Vector origin, IWorldGenLevel level, bool underwater,
        double angle, Vector center, int ellipseA, int ellipseC)
    {
        var extent = radius + 1 + ellipseA / 3;
        var c = Math.Min(radius - 3, 3) + ellipseC / 2 - 1;

        for (var x = -extent; x < extent; x++)
        {
            for (var z = -extent; z < extent; z++)
            {
                if (!(SignedDistanceEllipse(x, z, center, extent, c, angle) < 0.0))
                {
                    continue;
                }

                var position = origin + new Vector(x, y, z);
                var existing = level.GetBlock(position);
                if (!IsIcebergState(existing) && !existing.Is(BlockIds.SnowBlock))
                {
                    continue;
                }

                if (underwater)
                {
                    level.SetBlock(position, Water);
                }
                else
                {
                    level.SetBlock(position, Air);

                    if (level.GetBlock(position + Vector.Up).RegistryId == BlockIds.Snow)
                    {
                        level.SetBlock(position + Vector.Up, Air);
                    }
                }
            }
        }
    }

    private static void GenerateIcebergBlock(
        IWorldGenLevel level, IRandomSource random, Vector origin, int height, int x, int y, int z,
        int radius, int a, bool ellipse, int ellipseC, double angle, bool snowOnTop, IBlock state)
    {
        var distance = ellipse
            ? SignedDistanceEllipse(x, z, Vector.Zero, a, GetEllipseC(y, height, ellipseC), angle)
            : SignedDistanceCircle(x, z, Vector.Zero, radius, random);

        // A taper may have a zero ellipse axis. Java rejects its NaN distance as well as outside points.
        if (!(distance < 0.0))
        {
            return;
        }

        double threshold = ellipse ? -0.5 : -6 - random.NextInt(3);
        if (distance > threshold && random.NextDouble() > 0.9)
        {
            return;
        }

        SetIcebergBlock(origin + new Vector(x, y, z), level, random, height - y, height, ellipse, snowOnTop, state);
    }

    private static void SetIcebergBlock(
        Vector position, IWorldGenLevel level, IRandomSource random, int heightAbove, int height,
        bool ellipse, bool snowOnTop, IBlock state)
    {
        var existing = level.GetBlock(position);
        if (!existing.IsAir && existing.RegistryId is not (BlockIds.SnowBlock or BlockIds.Ice or BlockIds.Water))
        {
            return;
        }

        var snowAllowed = !ellipse || random.NextDouble() > 0.05;
        var divisor = ellipse ? 3 : 2;
        if (snowOnTop && !existing.Is(BlockIds.Water)
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
        {
            c -= 4 - (height - y);
        }

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

    private static bool IsIcebergState(IBlock block) =>
        block.RegistryId is BlockIds.PackedIce or BlockIds.SnowBlock or BlockIds.BlueIce;

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
                    if (!IsIcebergState(existing) && !existing.Is(BlockIds.Snow))
                    {
                        continue;
                    }

                    if (level.GetBlock(position + Vector.Down).IsAir)
                    {
                        level.SetBlock(position, Air);
                        level.SetBlock(position + Vector.Up, Air);
                    }
                    else if (IsIcebergState(existing))
                    {
                        var open = 0;
                        foreach (var face in FeatureHelpers.Horizontal)
                        {
                            if (!IsIcebergState(level.GetBlock(position.Offset(face))))
                            {
                                open++;
                            }
                        }

                        if (open >= 3)
                        {
                            level.SetBlock(position, Air);
                        }
                    }
                }
            }
        }
    }
}
