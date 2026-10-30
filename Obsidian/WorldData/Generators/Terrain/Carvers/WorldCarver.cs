using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Terrain.Carvers;

/// <summary>Geometry only: marks the union of tunnels before any terrain blocks are replaced.</summary>
internal abstract class WorldCarver<TConfiguration> where TConfiguration : CarverConfiguration
{
    /// <summary>
    /// Radius in chunks a carver started in one chunk can reach.
    /// </summary>
    protected const int Range = 4;

    /// <summary>
    /// Carves the parts of the carver started in (<paramref name="startChunkX"/>, <paramref name="startChunkZ"/>)
    /// that fall into the context's chunk.
    /// </summary>
    public abstract void Carve(
        CarvingContext context,
        TConfiguration configuration,
        IRandomSource random,
        int startChunkX,
        int startChunkZ);

    /// <summary>
    /// Skips positions inside the ellipsoid. A struct, so each carver's ellipsoid loop is specialized for its check.
    /// </summary>
    protected interface ISkipChecker
    {
        /// <param name="relativeX">X relative to the center, in horizontal radii.</param>
        /// <param name="relativeY">Y relative to the center, in vertical radii.</param>
        /// <param name="relativeZ">Z relative to the center, in horizontal radii.</param>
        /// <param name="y">The block Y.</param>
        public bool ShouldSkip(double relativeX, double relativeY, double relativeZ, int y);
    }

    protected void CarveEllipsoid<TSkipChecker>(
        CarvingContext context,
        TConfiguration configuration,
        double x,
        double y,
        double z,
        double horizontalRadius,
        double verticalRadius,
        TSkipChecker skipChecker) where TSkipChecker : struct, ISkipChecker
    {
        var chunk = context.Chunk;
        var chunkMinX = chunk.X << 4;
        var chunkMinZ = chunk.Z << 4;
        double middleX = chunkMinX + 8;
        double middleZ = chunkMinZ + 8;
        var reach = 16.0 + horizontalRadius * 2.0;

        if (Math.Abs(x - middleX) > reach || Math.Abs(z - middleZ) > reach)
        {
            return;
        }

        var minLocalX = Math.Max(Mth.Floor(x - horizontalRadius) - chunkMinX - 1, 0);
        var maxLocalX = Math.Min(Mth.Floor(x + horizontalRadius) - chunkMinX, 15);
        var minBlockY = Math.Max(Mth.Floor(y - verticalRadius) - 1, context.Mask.MinY);
        // Vanilla keeps the top 7 blocks uncarved (unless upgrading old chunks).
        var maxBlockY = Math.Min(Mth.Floor(y + verticalRadius) + 1, context.Mask.MaxY);
        var minLocalZ = Math.Max(Mth.Floor(z - horizontalRadius) - chunkMinZ - 1, 0);
        var maxLocalZ = Math.Min(Mth.Floor(z + horizontalRadius) - chunkMinZ, 15);

        if (maxBlockY <= minBlockY || maxLocalX < minLocalX || maxLocalZ < minLocalZ)
        {
            return;
        }

        // Each relative coordinate depends on its own axis, so compute it once per ellipsoid.
        Span<double> relativeZs = stackalloc double[maxLocalZ - minLocalZ + 1];
        for (var localZ = minLocalZ; localZ <= maxLocalZ; localZ++)
        {
            relativeZs[localZ - minLocalZ] = (chunkMinZ + localZ + 0.5 - z) / horizontalRadius;
        }

        Span<double> relativeYs = stackalloc double[maxBlockY - minBlockY];
        for (var blockY = maxBlockY; blockY > minBlockY; blockY--)
        {
            relativeYs[maxBlockY - blockY] = (blockY - 0.5 - y) / verticalRadius;
        }

        for (var localX = minLocalX; localX <= maxLocalX; localX++)
        {
            var blockX = chunkMinX + localX;
            var relativeX = (blockX + 0.5 - x) / horizontalRadius;

            for (var localZ = minLocalZ; localZ <= maxLocalZ; localZ++)
            {
                var relativeZ = relativeZs[localZ - minLocalZ];

                if (relativeX * relativeX + relativeZ * relativeZ >= 1.0)
                {
                    continue;
                }

                for (var blockY = maxBlockY; blockY > minBlockY; blockY--)
                {
                    var relativeY = relativeYs[maxBlockY - blockY];

                    if (skipChecker.ShouldSkip(relativeX, relativeY, relativeZ, blockY))
                    {
                        continue;
                    }

                    context.Mask.Set(localX, blockY, localZ);
                }
            }
        }
    }

    protected static bool CanReach(IChunk chunk, double x, double z, int branchIndex, int branchCount, float thickness)
    {
        var distanceX = x - ((chunk.X << 4) + 8);
        var distanceZ = z - ((chunk.Z << 4) + 8);
        double remaining = branchCount - branchIndex;
        double reach = thickness + 2.0f + 16.0f;

        return distanceX * distanceX + distanceZ * distanceZ - remaining * remaining <= reach * reach;
    }
}
