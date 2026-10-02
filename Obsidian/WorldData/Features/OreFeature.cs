using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features.RuleTests;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A blob of ore along a random line segment, replacing blocks matched by the targets. Mirrors vanilla's OreFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:ore")]
public sealed class OreFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:ore";

    /// <summary>
    /// Candidate replacements; the first whose rule matches the existing block is used.
    /// </summary>
    public required OreTarget[] Targets { get; init; }

    public required int Size { get; init; }

    /// <summary>
    /// Chance to skip a position that touches air (0 never skips, 1 never places next to air).
    /// </summary>
    public float DiscardChanceOnAirExposure { get; init; }

    public override bool Place(FeatureContext context)
    {
        var random = context.Random;
        var origin = context.Origin;
        var level = context.Level;

        var angle = random.NextFloat() * (float)Math.PI;
        var spread = this.Size / 8.0f;
        var radius = (int)Math.Ceiling((this.Size / 16.0f * 2.0f + 1.0f) / 2.0f);
        var startX = origin.X + Math.Sin(angle) * spread;
        var endX = origin.X - Math.Sin(angle) * spread;
        var startZ = origin.Z + Math.Cos(angle) * spread;
        var endZ = origin.Z - Math.Cos(angle) * spread;
        double startY = origin.Y + random.NextInt(3) - 2;
        double endY = origin.Y + random.NextInt(3) - 2;

        var spreadCeiling = (int)Math.Ceiling(spread);
        var minX = origin.X - spreadCeiling - radius;
        var minY = origin.Y - 2 - radius;
        var minZ = origin.Z - spreadCeiling - radius;
        var width = 2 * (spreadCeiling + radius);
        var height = 2 * (2 + radius);

        // Only place when some column of the area reaches down to the blob.
        for (var x = minX; x <= minX + width; x++)
        {
            for (var z = minZ; z <= minZ + width; z++)
            {
                if (minY <= level.GetHeight(HeightmapType.OceanFloorWG, x, z))
                    return this.DoPlace(level, random, startX, endX, startZ, endZ, startY, endY, minX, minY, minZ, width, height);
            }
        }

        return false;
    }

    // Stack buffers aren't zeroed implicitly: the spheres are written before they're read, and clearing the visited bits
    // explicitly is much faster.
    [SkipLocalsInit]
    private bool DoPlace(IWorldGenLevel level, IRandomSource random, double startX, double endX, double startZ, double endZ,
        double startY, double endY, int minX, int minY, int minZ, int width, int height)
    {
        var placed = 0;
        var size = this.Size;

        // Vanilla's sizes keep both buffers small enough for the stack.
        var visitedWords = (width * height * width + 63) >> 6;
        Span<ulong> visited = visitedWords <= 1024 ? stackalloc ulong[visitedWords] : new ulong[visitedWords];
        visited.Clear();
        Span<double> spheres = size <= 128 ? stackalloc double[size * 4] : new double[size * 4];

        for (var i = 0; i < size; i++)
        {
            var progress = (float)i / size;
            var x = startX + progress * (endX - startX);
            var y = startY + progress * (endY - startY);
            var z = startZ + progress * (endZ - startZ);
            var sizeFactor = random.NextDouble() * size / 16.0;
            var radius = ((Mth.Sin((float)Math.PI * progress) + 1.0f) * sizeFactor + 1.0) / 2.0;

            spheres[i * 4] = x;
            spheres[i * 4 + 1] = y;
            spheres[i * 4 + 2] = z;
            spheres[i * 4 + 3] = radius;
        }

        // Drop spheres fully contained in a bigger neighbor.
        for (var i = 0; i < size - 1; i++)
        {
            if (spheres[i * 4 + 3] <= 0.0)
                continue;

            for (var j = i + 1; j < size; j++)
            {
                if (spheres[j * 4 + 3] <= 0.0)
                    continue;

                var dx = spheres[i * 4] - spheres[j * 4];
                var dy = spheres[i * 4 + 1] - spheres[j * 4 + 1];
                var dz = spheres[i * 4 + 2] - spheres[j * 4 + 2];
                var dr = spheres[i * 4 + 3] - spheres[j * 4 + 3];

                if (dr * dr > dx * dx + dy * dy + dz * dz)
                {
                    if (dr > 0.0)
                        spheres[j * 4 + 3] = -1.0;
                    else
                        spheres[i * 4 + 3] = -1.0;
                }
            }
        }

        for (var i = 0; i < size; i++)
        {
            var radius = spheres[i * 4 + 3];
            if (radius < 0.0)
                continue;

            var centerX = spheres[i * 4];
            var centerY = spheres[i * 4 + 1];
            var centerZ = spheres[i * 4 + 2];
            var fromX = Math.Max(Mth.Floor(centerX - radius), minX);
            var fromY = Math.Max(Mth.Floor(centerY - radius), minY);
            var fromZ = Math.Max(Mth.Floor(centerZ - radius), minZ);
            var toX = Math.Max(Mth.Floor(centerX + radius), fromX);
            var toY = Math.Max(Mth.Floor(centerY + radius), fromY);
            var toZ = Math.Max(Mth.Floor(centerZ + radius), fromZ);

            for (var x = fromX; x <= toX; x++)
            {
                var relativeX = (x + 0.5 - centerX) / radius;
                if (relativeX * relativeX >= 1.0)
                    continue;

                for (var y = fromY; y <= toY; y++)
                {
                    var relativeY = (y + 0.5 - centerY) / radius;
                    if (relativeX * relativeX + relativeY * relativeY >= 1.0 || level.IsOutsideBuildHeight(y))
                        continue;

                    for (var z = fromZ; z <= toZ; z++)
                    {
                        var relativeZ = (z + 0.5 - centerZ) / radius;
                        if (relativeX * relativeX + relativeY * relativeY + relativeZ * relativeZ >= 1.0)
                            continue;

                        var index = x - minX + (y - minY) * width + (z - minZ) * width * height;
                        var bit = 1UL << index;
                        if ((visited[index >> 6] & bit) != 0)
                            continue;

                        visited[index >> 6] |= bit;
                        var position = new Vector(x, y, z);
                        if (!level.EnsureCanWrite(position))
                            continue;

                        var existing = level.GetStateId(position);
                        foreach (var target in this.Targets)
                        {
                            if (this.CanPlaceOre(level, existing, random, target, position))
                            {
                                level.SetBlock(position, target.Block);
                                placed++;
                                break;
                            }
                        }
                    }
                }
            }
        }

        return placed > 0;
    }

    private bool CanPlaceOre(IWorldGenLevel level, int existing, IRandomSource random, OreTarget target, Vector position)
    {
        if (!target.Test(existing, random))
            return false;

        return this.ShouldSkipAirCheck(random) || !IsAdjacentToAir(level, position);
    }

    private bool ShouldSkipAirCheck(IRandomSource random)
    {
        if (this.DiscardChanceOnAirExposure <= 0.0f)
            return true;

        return this.DiscardChanceOnAirExposure < 1.0f && random.NextFloat() >= this.DiscardChanceOnAirExposure;
    }

    private static bool IsAdjacentToAir(IWorldGenLevel level, Vector position)
    {
        foreach (var face in FeatureHelpers.Directions)
        {
            if (BlockPhysics.IsAir(level.GetStateId(position.Offset(face))))
                return true;
        }

        return false;
    }
}

/// <summary>
/// An ore block and the rule deciding which blocks it may replace.
/// </summary>
public sealed class OreTarget
{
    public required IRuleTest Target { get; init; }

    public required SimpleBlockState State { get; init; }

    /// <summary>
    /// Whether <see cref="Target"/> accepts the block with state id <paramref name="stateId"/>.
    /// </summary>
    internal bool Test(int stateId, IRandomSource random) => this.Target is IStateRuleTest stateTest
        ? stateTest.Test(stateId, random)
        : this.Target.Test(BlocksRegistry.Get(stateId), random);

    public IBlock Block => field ??= BlocksRegistry.GetFromSimpleState(this.State);
}
