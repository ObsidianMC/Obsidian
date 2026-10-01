using Obsidian.API.World.Generator.DensityFunctions;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Chunk-bound evaluation of a <see cref="RandomState"/>'s noise router, mirroring vanilla's NoiseChunk.
/// </summary>
/// <remarks>
/// Marker functions are replaced through <see cref="IDensityFunction.MapAll"/>:
/// <list type="bullet">
/// <item><c>interpolated</c> samples its argument once per cell corner and trilinearly interpolates.</item>
/// <item><c>flat_cache</c> and <c>cache_2d</c> cache per quart column and per block column.</item>
/// <item><c>cache_once</c> caches the last sampled position.</item>
/// </list>
/// Only interpolation changes values; the caches exist for speed. Instances are not thread-safe and
/// should live for the generation of a single chunk.
/// </remarks>
internal sealed class NoiseChunk
{
    private readonly Dictionary<long, int> preliminarySurfaceLevels = [];
    private readonly ChunkVisitor cellCacheMode;
    private readonly ChunkVisitor fillLoopMode;

    public RandomState RandomState { get; }

    public int CellWidth { get; }

    public int CellHeight { get; }

    public int MinY { get; }

    public int Height { get; }

    public int ChunkMinX { get; }

    public int ChunkMinZ { get; }

    /// <summary>
    /// Number of cells along X and Z.
    /// </summary>
    public int CellCountXZ { get; }

    /// <summary>
    /// Number of cells along Y.
    /// </summary>
    public int CellCountY { get; }

    /// <summary>
    /// Cell Y index of the lowest cell (<c>MinY / CellHeight</c>).
    /// </summary>
    public int CellNoiseMinY { get; }

    private int FirstCellX => Math.DivRem(this.ChunkMinX, this.CellWidth).Quotient;

    private int FirstCellZ => Math.DivRem(this.ChunkMinZ, this.CellWidth).Quotient;

    private int FirstQuartX => this.ChunkMinX >> 2;

    private int FirstQuartZ => this.ChunkMinZ >> 2;

    /// <summary>
    /// Final density with interpolation evaluated the way vanilla's per-cell cache does (X, then Y, then Z).
    /// </summary>
    public IDensityFunction FinalDensity => field ??= this.cellCacheMode.Map(this.RandomState.Router.FinalDensity);

    /// <summary>
    /// Ore vein functions with interpolation evaluated the way vanilla's fill loop does (Y, then X, then Z).
    /// </summary>
    public IDensityFunction VeinToggle => field ??= this.fillLoopMode.Map(this.RandomState.Router.VeinToggle);

    /// <inheritdoc cref="VeinToggle"/>
    public IDensityFunction VeinRidged => field ??= this.fillLoopMode.Map(this.RandomState.Router.VeinRidged);

    /// <inheritdoc cref="VeinToggle"/>
    public IDensityFunction VeinGap => field ??= this.fillLoopMode.Map(this.RandomState.Router.VeinGap);

    /// <summary>
    /// Router functions used by aquifers. Erosion and depth go through the chunk's flat caches like in vanilla,
    /// so positions inside the chunk sample their quart column.
    /// </summary>
    public IDensityFunction Erosion => field ??= this.fillLoopMode.Map(this.RandomState.Router.Erosion);

    /// <inheritdoc cref="Erosion"/>
    public IDensityFunction Depth => field ??= this.fillLoopMode.Map(this.RandomState.Router.Depth);

    /// <inheritdoc cref="Erosion"/>
    public IDensityFunction Barrier => field ??= this.fillLoopMode.Map(this.RandomState.Router.Barrier);

    /// <inheritdoc cref="Erosion"/>
    public IDensityFunction FluidLevelFloodedness => field ??= this.fillLoopMode.Map(this.RandomState.Router.FluidLevelFloodedness);

    /// <inheritdoc cref="Erosion"/>
    public IDensityFunction FluidLevelSpread => field ??= this.fillLoopMode.Map(this.RandomState.Router.FluidLevelSpread);

    /// <inheritdoc cref="Erosion"/>
    public IDensityFunction Lava => field ??= this.fillLoopMode.Map(this.RandomState.Router.Lava);

    /// <summary>
    /// Climate sampler sharing the chunk's flat caches, like vanilla's cached climate sampler.
    /// Biome positions are quart aligned, so the caches return the same values as direct sampling.
    /// </summary>
    public ClimateSampler ClimateSampler => field ??= new ClimateSampler(
        this.fillLoopMode.Map(this.RandomState.Router.Temperature),
        this.fillLoopMode.Map(this.RandomState.Router.Vegetation),
        this.fillLoopMode.Map(this.RandomState.Router.Continents),
        this.Erosion,
        this.Depth,
        this.fillLoopMode.Map(this.RandomState.Router.Ridges));

    private IDensityFunction PreliminarySurfaceLevelFunction => field ??= this.fillLoopMode.Map(this.RandomState.Router.PreliminarySurfaceLevel);

    public NoiseChunk(RandomState randomState, int chunkX, int chunkZ)
    {
        this.RandomState = randomState;

        var noise = randomState.Settings.Noise;
        this.CellWidth = noise.SizeHorizontal * 4;
        this.CellHeight = noise.SizeVertical * 4;
        this.MinY = noise.MinY;
        this.Height = noise.Height;
        this.ChunkMinX = chunkX << 4;
        this.ChunkMinZ = chunkZ << 4;
        this.CellCountXZ = 16 / this.CellWidth;
        this.CellCountY = Math.DivRem(this.Height, this.CellHeight).Quotient;
        this.CellNoiseMinY = (int)Math.Floor((double)this.MinY / this.CellHeight);

        this.cellCacheMode = new ChunkVisitor(this, InterpolationOrder.CellCache);
        this.fillLoopMode = new ChunkVisitor(this, InterpolationOrder.FillLoop);
    }

    /// <summary>
    /// Preliminary surface height of the quart column containing (<paramref name="x"/>, <paramref name="z"/>).
    /// </summary>
    public int PreliminarySurfaceLevel(int x, int z)
    {
        var quartX = x & ~3;
        var quartZ = z & ~3;
        var key = ((long)quartX & 0xFFFFFFFFL) | (((long)quartZ & 0xFFFFFFFFL) << 32);

        if (!this.preliminarySurfaceLevels.TryGetValue(key, out var level))
        {
            level = (int)Math.Floor(this.PreliminarySurfaceLevelFunction.GetValue(quartX, 0, quartZ));
            this.preliminarySurfaceLevels[key] = level;
        }

        return level;
    }

    /// <summary>
    /// Highest preliminary surface level sampled every 4 blocks in the inclusive block area.
    /// </summary>
    public int MaxPreliminarySurfaceLevel(int minX, int minZ, int maxX, int maxZ)
    {
        var max = int.MinValue;

        for (var z = minZ; z <= maxZ; z += 4)
        {
            for (var x = minX; x <= maxX; x += 4)
                max = Math.Max(max, this.PreliminarySurfaceLevel(x, z));
        }

        return max;
    }

    private enum InterpolationOrder
    {
        /// <summary>
        /// Mth.lerp3 order, used while vanilla fills its per-cell final density cache.
        /// </summary>
        CellCache,

        /// <summary>
        /// updateForY/X/Z order, used by everything sampled directly in vanilla's fill loop.
        /// </summary>
        FillLoop
    }

    private sealed class ChunkVisitor : IDensityFunctionVisitor
    {
        private readonly NoiseChunk chunk;
        private readonly InterpolationOrder order;

        // Shared router subtrees map to one chunk-bound node, so their caches are shared too.
        private readonly Dictionary<IDensityFunction, IDensityFunction> mapped = new(ReferenceEqualityComparer.Instance);

        public ChunkVisitor(NoiseChunk chunk, InterpolationOrder order)
        {
            this.chunk = chunk;
            this.order = order;
        }

        public IDensityFunction Map(IDensityFunction function)
        {
            if (!this.mapped.TryGetValue(function, out var result))
            {
                result = function.MapAll(this);
                this.mapped[function] = result;
            }

            return result;
        }

        public IDensityFunction Apply(IDensityFunction function) => function switch
        {
            InterpolatedDensityFunction interpolated => new CellInterpolator(this.chunk, interpolated.Argument, this.order),
            FlatCacheDensityFunction flatCache => new FlatCache(this.chunk, flatCache.Argument),
            Cache2DDensityFunction cache2D => new Cache2D(cache2D.Argument),
            CacheOnceDensityFunction cacheOnce => new CacheOnce(cacheOnce.Argument),
            _ => function
        };
    }

    /// <summary>
    /// Samples its argument at every cell corner of the chunk on first use, then interpolates inside cells.
    /// Positions outside the chunk are evaluated directly.
    /// </summary>
    private sealed class CellInterpolator : IDensityFunction
    {
        private readonly NoiseChunk chunk;
        private readonly IDensityFunction argument;
        private readonly InterpolationOrder order;

        public string Type => "minecraft:interpolated";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        private double[,,] Corners => field ??= this.SampleCorners();

        public CellInterpolator(NoiseChunk chunk, IDensityFunction argument, InterpolationOrder order)
        {
            this.chunk = chunk;
            this.argument = argument;
            this.order = order;
        }

        public double GetValue(double x, double y, double z)
        {
            var chunk = this.chunk;
            var localX = (int)x - chunk.ChunkMinX;
            var localZ = (int)z - chunk.ChunkMinZ;
            var localY = (int)y - chunk.CellNoiseMinY * chunk.CellHeight;

            if (localX is < 0 or >= 16 || localZ is < 0 or >= 16 || localY < 0 || localY >= chunk.CellCountY * chunk.CellHeight)
                return this.argument.GetValue(x, y, z);

            var (cellX, inCellX) = Math.DivRem(localX, chunk.CellWidth);
            var (cellY, inCellY) = Math.DivRem(localY, chunk.CellHeight);
            var (cellZ, inCellZ) = Math.DivRem(localZ, chunk.CellWidth);

            var corners = this.Corners;
            var n000 = corners[cellX, cellY, cellZ];
            var n100 = corners[cellX + 1, cellY, cellZ];
            var n010 = corners[cellX, cellY + 1, cellZ];
            var n110 = corners[cellX + 1, cellY + 1, cellZ];
            var n001 = corners[cellX, cellY, cellZ + 1];
            var n101 = corners[cellX + 1, cellY, cellZ + 1];
            var n011 = corners[cellX, cellY + 1, cellZ + 1];
            var n111 = corners[cellX + 1, cellY + 1, cellZ + 1];

            var deltaX = (double)inCellX / chunk.CellWidth;
            var deltaY = (double)inCellY / chunk.CellHeight;
            var deltaZ = (double)inCellZ / chunk.CellWidth;

            // The two orders round differently, and vanilla uses both.
            if (this.order == InterpolationOrder.CellCache)
            {
                var bottom = Lerp(deltaY, Lerp(deltaX, n000, n100), Lerp(deltaX, n010, n110));
                var top = Lerp(deltaY, Lerp(deltaX, n001, n101), Lerp(deltaX, n011, n111));
                return Lerp(deltaZ, bottom, top);
            }

            var z0 = Lerp(deltaX, Lerp(deltaY, n000, n010), Lerp(deltaY, n100, n110));
            var z1 = Lerp(deltaX, Lerp(deltaY, n001, n011), Lerp(deltaY, n101, n111));
            return Lerp(deltaZ, z0, z1);
        }

        private double[,,] SampleCorners()
        {
            var chunk = this.chunk;
            var corners = new double[chunk.CellCountXZ + 1, chunk.CellCountY + 1, chunk.CellCountXZ + 1];

            for (var cellX = 0; cellX <= chunk.CellCountXZ; cellX++)
            {
                var blockX = (chunk.FirstCellX + cellX) * chunk.CellWidth;

                for (var cellZ = 0; cellZ <= chunk.CellCountXZ; cellZ++)
                {
                    var blockZ = (chunk.FirstCellZ + cellZ) * chunk.CellWidth;

                    for (var cellY = 0; cellY <= chunk.CellCountY; cellY++)
                    {
                        var blockY = (chunk.CellNoiseMinY + cellY) * chunk.CellHeight;
                        corners[cellX, cellY, cellZ] = this.argument.GetValue(blockX, blockY, blockZ);
                    }
                }
            }

            return corners;
        }

        private static double Lerp(double delta, double start, double end) => start + delta * (end - start);
    }

    /// <summary>
    /// Values at y = 0 for every quart column of the chunk (plus one past the edge), like vanilla's flat cache.
    /// </summary>
    private sealed class FlatCache : IDensityFunction
    {
        private readonly NoiseChunk chunk;
        private readonly IDensityFunction argument;
        private readonly double[] values;
        private readonly int size;

        public string Type => "minecraft:flat_cache";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        public FlatCache(NoiseChunk chunk, IDensityFunction argument)
        {
            this.chunk = chunk;
            this.argument = argument;

            this.size = (chunk.CellCountXZ * chunk.CellWidth >> 2) + 1;
            this.values = new double[this.size * this.size];

            for (var quartX = 0; quartX < this.size; quartX++)
            {
                for (var quartZ = 0; quartZ < this.size; quartZ++)
                {
                    this.values[quartX + quartZ * this.size] = argument.GetValue(
                        (chunk.FirstQuartX + quartX) << 2, 0, (chunk.FirstQuartZ + quartZ) << 2);
                }
            }
        }

        public double GetValue(double x, double y, double z)
        {
            var quartX = ((int)x >> 2) - this.chunk.FirstQuartX;
            var quartZ = ((int)z >> 2) - this.chunk.FirstQuartZ;

            return quartX >= 0 && quartZ >= 0 && quartX < this.size && quartZ < this.size
                ? this.values[quartX + quartZ * this.size]
                : this.argument.GetValue(x, y, z);
        }
    }

    /// <summary>
    /// Remembers the value of the last block column, ignoring Y, like vanilla's 2D cache.
    /// </summary>
    private sealed class Cache2D : IDensityFunction
    {
        private readonly IDensityFunction argument;
        private int lastX = int.MinValue;
        private int lastZ = int.MinValue;
        private double lastValue;

        public string Type => "minecraft:cache_2d";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        public Cache2D(IDensityFunction argument) => this.argument = argument;

        public double GetValue(double x, double y, double z)
        {
            var blockX = (int)x;
            var blockZ = (int)z;

            if (blockX == this.lastX && blockZ == this.lastZ)
                return this.lastValue;

            this.lastX = blockX;
            this.lastZ = blockZ;
            this.lastValue = this.argument.GetValue(x, y, z);
            return this.lastValue;
        }
    }

    /// <summary>
    /// Remembers the value of the last sampled position, like vanilla's cache once.
    /// </summary>
    private sealed class CacheOnce : IDensityFunction
    {
        private readonly IDensityFunction argument;
        private double lastX = double.NaN;
        private double lastY;
        private double lastZ;
        private double lastValue;

        public string Type => "minecraft:cache_once";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        public CacheOnce(IDensityFunction argument) => this.argument = argument;

        public double GetValue(double x, double y, double z)
        {
            if (x == this.lastX && y == this.lastY && z == this.lastZ)
                return this.lastValue;

            this.lastX = x;
            this.lastY = y;
            this.lastZ = z;
            this.lastValue = this.argument.GetValue(x, y, z);
            return this.lastValue;
        }
    }
}
