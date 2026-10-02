using Obsidian.API.World.Generator.RandomSources;
using System.Collections;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// State shared by every carver run on one chunk.
/// </summary>
internal sealed class CarvingContext
{
    private readonly SurfaceBuilder surfaceBuilder;

    public required IChunk Chunk { get; init; }

    public required NoiseChunk NoiseChunk { get; init; }

    public required IAquifer Aquifer { get; init; }

    /// <summary>
    /// Block-level biomes from the biome source (not the chunk's storage), like vanilla's carver biome getter.
    /// </summary>
    public required BiomeManager Biomes { get; init; }

    public CarvingMask Mask { get; }

    /// <summary>
    /// Receives carved fluid positions that need an update to settle, like vanilla's post-processing marks.
    /// </summary>
    public ICollection<Vector>? FluidUpdates { get; init; }

    public int MinY { get; }

    public int Height { get; }

    public CarvingContext(SurfaceBuilder surfaceBuilder, int minY, int height)
    {
        this.surfaceBuilder = surfaceBuilder;
        this.MinY = minY;
        this.Height = height;
        this.Mask = new CarvingMask(minY, height);
    }

    /// <summary>
    /// The surface block for a dirt block uncovered by carving, e.g. grass where the carver cut away the top.
    /// </summary>
    public IBlock? TopMaterial(int x, int y, int z, bool fluidAbove) =>
        this.surfaceBuilder.TopMaterial(this.Chunk, this.NoiseChunk, this.Biomes, x, y, z, fluidAbove);
}

/// <summary>
/// Positions already carved in a chunk, so overlapping carvers don't carve them twice.
/// </summary>
internal sealed class CarvingMask(int minY, int height)
{
    private readonly int minY = minY;
    private readonly BitArray mask = new(256 * height);

    public bool Get(int localX, int y, int localZ) => this.mask[this.GetIndex(localX, y, localZ)];

    public void Set(int localX, int y, int localZ) => this.mask[this.GetIndex(localX, y, localZ)] = true;

    private int GetIndex(int localX, int y, int localZ) => (localX & 15) | (localZ & 15) << 4 | (y - this.minY) << 8;
}

/// <summary>
/// Base of the classic tunnel carvers, mirroring vanilla's WorldCarver.
/// </summary>
/// <remarks>
/// Vanilla simulates a start chunk's carver again for every chunk within reach. Here a carver first plans what a start
/// chunk carves (the tunnels' paths and ellipsoids, which don't depend on the carved chunk), and each chunk replays the
/// plan, stopping tunnels where the simulation for that chunk would stop them. Plans can be kept and reused.
/// </remarks>
/// <typeparam name="TPlan">What the carver started in one chunk carves.</typeparam>
internal abstract class WorldCarver<TConfiguration, TPlan> where TConfiguration : CarverConfiguration
{
    /// <summary>
    /// Radius in chunks a carver started in one chunk can reach.
    /// </summary>
    protected const int Range = 4;

    /// <summary>
    /// Decides whether a chunk starts this carver.
    /// </summary>
    public bool IsStartChunk(TConfiguration configuration, IRandomSource random) => random.NextFloat() <= configuration.Probability;

    /// <summary>
    /// Plans the carver started in (<paramref name="startChunkX"/>, <paramref name="startChunkZ"/>), drawing from the random
    /// that <see cref="IsStartChunk"/> just used.
    /// </summary>
    public abstract TPlan Plan(TConfiguration configuration, IRandomSource random, int startChunkX, int startChunkZ, int minY, int height);

    /// <summary>
    /// Carves the parts of a plan that fall into the context's chunk, exactly as vanilla's simulation for that chunk does.
    /// </summary>
    public abstract void Carve(CarvingContext context, TConfiguration configuration, TPlan plan);

    /// <summary>
    /// Carves a tunnel's ellipsoids until one from which the tunnel can no longer reach the context's chunk (where
    /// vanilla's simulation returns), then its branches.
    /// </summary>
    protected void CarveTunnel<TSkipChecker>(CarvingContext context, TConfiguration configuration, Tunnel tunnel, TSkipChecker skipChecker)
        where TSkipChecker : struct, ISkipChecker
    {
        foreach (var step in tunnel.Steps)
        {
            if (!CanReach(context.Chunk, step.X, step.Z, step.Index, tunnel.BranchCount, tunnel.Thickness))
                return;

            this.CarveEllipsoid(context, configuration, step.X, step.Y, step.Z, step.HorizontalRadius, step.VerticalRadius, skipChecker);
        }

        if (tunnel.Branches is { } branches)
        {
            this.CarveTunnel(context, configuration, branches.Left, skipChecker);
            this.CarveTunnel(context, configuration, branches.Right, skipChecker);
        }
    }

    /// <summary>
    /// The ellipsoids a tunnel carves along its path, in order, and the tunnels it splits into after the last one.
    /// </summary>
    /// <param name="branchIndex">The index of the tunnel's first step, which sizes <see cref="Steps"/>.</param>
    internal sealed class Tunnel(int branchIndex, int branchCount, float thickness)
    {
        public int BranchCount { get; } = branchCount;

        public float Thickness { get; } = thickness;

        public List<TunnelStep> Steps { get; } = new(Math.Max(branchCount - branchIndex, 0));

        public (Tunnel Left, Tunnel Right)? Branches { get; set; }
    }

    /// <summary>
    /// An ellipsoid carved at step <see cref="Index"/> of a tunnel (or as a cave's room).
    /// </summary>
    internal readonly record struct TunnelStep(int Index, double X, double Y, double Z, double HorizontalRadius, double VerticalRadius);

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

        /// <summary>
        /// Bounds on the relative Y of the positions of a column that aren't skipped, given its relative X and Z (whose
        /// squares sum to less than 1). They may be loose, up to rounding.
        /// </summary>
        public (double Min, double Max) RelativeYBounds(double relativeX, double relativeZ);
    }

    protected void CarveEllipsoid<TSkipChecker>(CarvingContext context, TConfiguration configuration, double x, double y, double z,
        double horizontalRadius, double verticalRadius, TSkipChecker skipChecker) where TSkipChecker : struct, ISkipChecker
    {
        var chunk = context.Chunk;
        var chunkMinX = chunk.X << 4;
        var chunkMinZ = chunk.Z << 4;
        double middleX = chunkMinX + 8;
        double middleZ = chunkMinZ + 8;
        var reach = 16.0 + horizontalRadius * 2.0;

        if (Math.Abs(x - middleX) > reach || Math.Abs(z - middleZ) > reach)
            return;

        var minLocalX = Math.Max(Mth.Floor(x - horizontalRadius) - chunkMinX - 1, 0);
        var maxLocalX = Math.Min(Mth.Floor(x + horizontalRadius) - chunkMinX, 15);
        var minBlockY = Math.Max(Mth.Floor(y - verticalRadius) - 1, context.MinY + 1);
        // Vanilla keeps the top 7 blocks uncarved (unless upgrading old chunks).
        var maxBlockY = Math.Min(Mth.Floor(y + verticalRadius) + 1, context.MinY + context.Height - 1 - 7);
        var minLocalZ = Math.Max(Mth.Floor(z - horizontalRadius) - chunkMinZ - 1, 0);
        var maxLocalZ = Math.Min(Mth.Floor(z + horizontalRadius) - chunkMinZ, 15);

        if (maxBlockY <= minBlockY || maxLocalX < minLocalX || maxLocalZ < minLocalZ)
            return;

        // Each relative coordinate only depends on its own axis, so it's computed once per ellipsoid rather than per block.
        Span<double> relativeZs = stackalloc double[maxLocalZ - minLocalZ + 1];
        for (var localZ = minLocalZ; localZ <= maxLocalZ; localZ++)
            relativeZs[localZ - minLocalZ] = (chunkMinZ + localZ + 0.5 - z) / horizontalRadius;

        Span<double> relativeYs = stackalloc double[maxBlockY - minBlockY];
        for (var blockY = maxBlockY; blockY > minBlockY; blockY--)
            relativeYs[maxBlockY - blockY] = (blockY - 0.5 - y) / verticalRadius;

        for (var localX = minLocalX; localX <= maxLocalX; localX++)
        {
            var blockX = chunkMinX + localX;
            var relativeX = (blockX + 0.5 - x) / horizontalRadius;

            for (var localZ = minLocalZ; localZ <= maxLocalZ; localZ++)
            {
                var relativeZ = relativeZs[localZ - minLocalZ];

                if (relativeX * relativeX + relativeZ * relativeZ >= 1.0)
                    continue;

                // Set once the column passes through grass or mycelium, so exposed dirt below gets a top block.
                var surfaceReached = false;

                // Positions outside the skip check's bounds are skipped anyway; a block to spare on each side covers rounding.
                var (minRelativeY, maxRelativeY) = skipChecker.RelativeYBounds(relativeX, relativeZ);
                var top = Math.Min(maxBlockY, Math.Ceiling(y + 0.5 + maxRelativeY * verticalRadius) + 1.0);
                var bottom = Math.Max(minBlockY, Math.Floor(y + 0.5 + minRelativeY * verticalRadius) - 1.0);

                for (var blockY = (int)top; blockY > bottom; blockY--)
                {
                    var relativeY = relativeYs[maxBlockY - blockY];

                    if (skipChecker.ShouldSkip(relativeX, relativeY, relativeZ, blockY) || context.Mask.Get(localX, blockY, localZ))
                        continue;

                    context.Mask.Set(localX, blockY, localZ);
                    this.CarveBlock(context, configuration, localX, blockY, localZ, ref surfaceReached);
                }
            }
        }
    }

    /// <summary>
    /// Carves one block inside the ellipsoid.
    /// </summary>
    /// <param name="surfaceReached">Set once the column passes through grass or mycelium.</param>
    protected virtual void CarveBlock(CarvingContext context, TConfiguration configuration, int localX, int y, int localZ, ref bool surfaceReached)
    {
        var chunk = context.Chunk;
        var block = chunk.GetBlock(localX, y, localZ);

        if (block.Material is Material.GrassBlock or Material.Mycelium)
            surfaceReached = true;

        if (!configuration.CanReplace(block))
            return;

        var x = (chunk.X << 4) + localX;
        var z = (chunk.Z << 4) + localZ;
        var carved = this.GetCarveState(context, configuration, x, y, z);
        if (carved is null)
            return;

        chunk.SetBlock(localX, y, localZ, carved);

        // Like vanilla, this reads the aquifer's last answer even when the lava level decided the block.
        if (carved.IsLiquid && context.Aquifer.ShouldScheduleFluidUpdate)
            context.FluidUpdates?.Add(new Vector(x, y, z));

        if (!surfaceReached || y - 1 < context.MinY || chunk.GetBlock(localX, y - 1, localZ).Material != Material.Dirt)
            return;

        if (context.TopMaterial(x, y - 1, z, carved.IsLiquid) is IBlock topMaterial)
        {
            chunk.SetBlock(localX, y - 1, localZ, topMaterial);

            if (topMaterial.IsLiquid)
                context.FluidUpdates?.Add(new Vector(x, y - 1, z));
        }
    }

    private IBlock? GetCarveState(CarvingContext context, TConfiguration configuration, int x, int y, int z)
    {
        if (y <= configuration.LavaLevel.Resolve(context.MinY, context.Height))
            return BlocksRegistry.Lava;

        // A null substance means the aquifer wants a barrier here, so the block is left alone.
        return context.Aquifer.ComputeSubstance(x, y, z, 0.0);
    }

    /// <summary>
    /// Whether a tunnel at its current point can still reach the context's chunk before it ends.
    /// </summary>
    protected static bool CanReach(IChunk chunk, double x, double z, int branchIndex, int branchCount, float thickness)
    {
        var distanceX = x - ((chunk.X << 4) + 8);
        var distanceZ = z - ((chunk.Z << 4) + 8);
        double remaining = branchCount - branchIndex;
        double reach = thickness + 2.0f + 16.0f;

        return distanceX * distanceX + distanceZ * distanceZ - remaining * remaining <= reach * reach;
    }
}
