using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Ore(JsonElement json, bool scattered)
    {
        var ore = new OreBlob(
            json.GetProperty("targets").EnumerateArray().Select(this.OreTarget).ToArray(),
            json.GetProperty("size").GetInt32(),
            json.GetProperty("discard_chance_on_air_exposure").GetSingle());

        if (scattered)
        {
            return new ScatteredOre(
                json.GetProperty("targets").EnumerateArray().Select(this.OreTarget).ToArray(),
                json.GetProperty("size").GetInt32(),
                json.GetProperty("discard_chance_on_air_exposure").GetSingle()).Place;
        }

        return ore.Place;
    }

    private (RuleTest Target, IBlock State) OreTarget(JsonElement json) =>
        (this.Rule(json.GetProperty("target")), ReadBlock(json.GetProperty("state")));
}

/// <summary>
/// Vanilla's OreFeature: a line of spheres between two random points, whose blocks take the first target that
/// accepts them. 26.3 rule tests also see the position, which <c>height_match</c> uses to choose between stone and
/// deepslate ores.
/// </summary>
internal sealed class OreBlob((RuleTest Target, IBlock State)[] targets, int size, float discardChance)
{
    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;
        var angle = random.NextFloat() * (float)Math.PI;
        var spread = size / 8.0f;
        var radius = (int)Math.Ceiling((size / 16.0f * 2.0f + 1.0f) / 2.0f);
        var startX = origin.X + Math.Sin(angle) * spread;
        var endX = origin.X - Math.Sin(angle) * spread;
        var startZ = origin.Z + Math.Cos(angle) * spread;
        var endZ = origin.Z - Math.Cos(angle) * spread;
        double startY = origin.Y + random.NextInt(3) - 2;
        double endY = origin.Y + random.NextInt(3) - 2;

        var spreadCeiling = (int)Math.Ceiling(spread);
        var reach = spreadCeiling + radius;
        var min = new Vector(origin.X - reach, origin.Y - 2 - radius, origin.Z - reach);
        var width = 2 * (spreadCeiling + radius);
        var height = 2 * (2 + radius);

        // Only places when some column of the area reaches down to the blob.
        for (var x = min.X; x <= min.X + width; x++)
        {
            for (var z = min.Z; z <= min.Z + width; z++)
            {
                if (min.Y > level.GetHeight(HeightmapType.OceanFloorWG, x, z))
                {
                    continue;
                }

                var line = new OreLine(startX, endX, startY, endY, startZ, endZ);
                return this.DoPlace(level, random, line, min, width, height);
            }
        }

        return false;
    }

    private bool DoPlace(IWorldGenLevel level, IRandomSource random, OreLine line, Vector min, int width, int height)
    {
        var spheres = this.Spheres(random, line);
        var visited = new bool[width * height * width];
        var placed = 0;

        foreach (var (centerX, centerY, centerZ, radius) in spheres)
        {
            if (radius < 0.0)
            {
                continue;
            }

            var from = new Vector(
                Math.Max(Mth.Floor(centerX - radius), min.X),
                Math.Max(Mth.Floor(centerY - radius), min.Y),
                Math.Max(Mth.Floor(centerZ - radius), min.Z));
            var to = new Vector(
                Math.Max(Mth.Floor(centerX + radius), from.X),
                Math.Max(Mth.Floor(centerY + radius), from.Y),
                Math.Max(Mth.Floor(centerZ + radius), from.Z));

            for (var x = from.X; x <= to.X; x++)
            {
                var relativeX = (x + 0.5 - centerX) / radius;
                var distanceX = relativeX * relativeX;

                if (distanceX >= 1.0)
                {
                    continue;
                }

                for (var y = from.Y; y <= to.Y; y++)
                {
                    var relativeY = (y + 0.5 - centerY) / radius;
                    var distanceXY = distanceX + relativeY * relativeY;

                    if (distanceXY >= 1.0 || level.IsOutsideBuildHeight(y))
                    {
                        continue;
                    }

                    for (var z = from.Z; z <= to.Z; z++)
                    {
                        var relativeZ = (z + 0.5 - centerZ) / radius;

                        if (distanceXY + relativeZ * relativeZ >= 1.0)
                        {
                            continue;
                        }

                        var index = x - min.X + (y - min.Y) * width + (z - min.Z) * width * height;

                        if (visited[index])
                        {
                            continue;
                        }

                        visited[index] = true;

                        if (this.TryPlace(level, random, new Vector(x, y, z)))
                        {
                            placed++;
                        }
                    }
                }
            }
        }

        return placed > 0;
    }

    // The spheres along the line, with those inside a bigger neighbour dropped (radius -1).
    private (double X, double Y, double Z, double Radius)[] Spheres(IRandomSource random, OreLine line)
    {
        var spheres = new (double X, double Y, double Z, double Radius)[size];

        for (var i = 0; i < size; i++)
        {
            var progress = (float)i / size;
            var x = line.StartX + progress * (line.EndX - line.StartX);
            var y = line.StartY + progress * (line.EndY - line.StartY);
            var z = line.StartZ + progress * (line.EndZ - line.StartZ);
            var sizeFactor = random.NextDouble() * size / 16.0;
            var radius = ((Mth.Sin((float)Math.PI * progress) + 1.0f) * sizeFactor + 1.0) / 2.0;
            spheres[i] = (x, y, z, radius);
        }

        for (var i = 0; i < size - 1; i++)
        {
            for (var j = i + 1; j < size; j++)
            {
                if (spheres[i].Radius <= 0.0 || spheres[j].Radius <= 0.0)
                {
                    continue;
                }

                var dx = spheres[i].X - spheres[j].X;
                var dy = spheres[i].Y - spheres[j].Y;
                var dz = spheres[i].Z - spheres[j].Z;
                var dr = spheres[i].Radius - spheres[j].Radius;

                if (dr * dr <= dx * dx + dy * dy + dz * dz)
                {
                    continue;
                }

                if (dr > 0.0)
                {
                    spheres[j].Radius = -1.0;
                }
                else
                {
                    spheres[i].Radius = -1.0;
                }
            }
        }

        return spheres;
    }

    private bool TryPlace(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        if (!level.EnsureCanWrite(position))
        {
            return false;
        }

        var existing = level.GetBlock(position);

        foreach (var (target, state) in targets)
        {
            if (!target(existing, position, random))
            {
                continue;
            }

            if (!this.SkipsAirCheck(random) && IsAdjacentToAir(level, position))
            {
                continue;
            }

            level.SetBlock(position, state);
            return true;
        }

        return false;
    }

    private bool SkipsAirCheck(IRandomSource random)
    {
        if (discardChance <= 0.0f)
        {
            return true;
        }

        return discardChance < 1.0f && random.NextFloat() >= discardChance;
    }

    private static bool IsAdjacentToAir(IWorldGenLevel level, Vector position) =>
        FeatureHelpers.Directions.Any(face => FeatureHelpers.IsAirAt(level, position.Offset(face)));

    private readonly record struct OreLine(
        double StartX,
        double EndX,
        double StartY,
        double EndY,
        double StartZ,
        double EndZ);
}
