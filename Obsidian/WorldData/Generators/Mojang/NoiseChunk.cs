using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.RandomSources;

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
    private readonly CellGrid grid;

    // Lowest block Y and height of the area covered by whole cells.
    private readonly int filledMinY;
    private readonly int filledHeight;

    // Index steps between neighboring corners along Z and X; corners along Y are adjacent.
    private readonly int cornerStrideZ;
    private readonly int cornerStrideX;

    private CellFiller? finalDensityFiller;
    private CellFiller? veinToggleFiller;

    public RandomState RandomState { get; }

    public int CellWidth { get; }

    public int CellHeight { get; }

    public int MinY { get; }

    public int Height { get; }

    public int ChunkMinX { get; }

    public int ChunkMinZ { get; }

    /// <summary>
    /// Number of whole cells along X and Z. With a cell width that doesn't divide 16, the remaining columns
    /// aren't filled, matching vanilla.
    /// </summary>
    public int CellCountXZ { get; }

    /// <summary>
    /// Width in blocks of the area covered by whole cells.
    /// </summary>
    public int FilledWidth => this.CellCountXZ * this.CellWidth;

    /// <summary>
    /// Number of cells along Y.
    /// </summary>
    public int CellCountY { get; }

    /// <summary>
    /// Cell Y index of the lowest cell (<c>MinY / CellHeight</c>).
    /// </summary>
    public int CellNoiseMinY { get; }

    /// <summary>
    /// Number of blocks in a cell, i.e. the length of the spans the cell fill methods write.
    /// </summary>
    public int CellSize => this.CellWidth * this.CellWidth * this.CellHeight;

    private int FirstCellX => (int)Math.Floor((double)this.ChunkMinX / this.CellWidth);

    private int FirstCellZ => (int)Math.Floor((double)this.ChunkMinZ / this.CellWidth);

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

    /// <summary>
    /// The chunk's aquifer, shared by the steps that use the noise chunk like vanilla's, so its caches carry over.
    /// </summary>
    public IAquifer Aquifer => field ??= Aquifers.Create(this, this.ChunkMinX >> 4, this.ChunkMinZ >> 4, this.RandomState.GlobalFluidPicker);

    private IDensityFunction PreliminarySurfaceLevelFunction => field ??= this.fillLoopMode.Map(this.RandomState.Router.PreliminarySurfaceLevel);

    public NoiseChunk(RandomState randomState, int chunkX, int chunkZ) : this(randomState, chunkX << 4, chunkZ << 4, null)
    {
    }

    /// <param name="minX">X of the first cell; a multiple of the cell width.</param>
    /// <param name="minZ">Z of the first cell; a multiple of the cell width.</param>
    /// <param name="cellCountXZ">Cells along X and Z, or <c>null</c> for a whole chunk.</param>
    private NoiseChunk(RandomState randomState, int minX, int minZ, int? cellCountXZ)
    {
        this.RandomState = randomState;

        var noise = randomState.Settings.Noise;
        this.CellWidth = noise.SizeHorizontal * 4;
        this.CellHeight = noise.SizeVertical * 4;
        this.MinY = noise.MinY;
        this.Height = noise.Height;
        this.ChunkMinX = minX;
        this.ChunkMinZ = minZ;
        this.CellCountXZ = cellCountXZ ?? 16 / this.CellWidth;
        this.CellCountY = Math.DivRem(this.Height, this.CellHeight).Quotient;
        this.CellNoiseMinY = (int)Math.Floor((double)this.MinY / this.CellHeight);

        this.filledMinY = this.CellNoiseMinY * this.CellHeight;
        this.filledHeight = this.CellCountY * this.CellHeight;
        this.cornerStrideZ = this.CellCountY + 1;
        this.cornerStrideX = (this.CellCountXZ + 1) * this.cornerStrideZ;
        this.grid = CellGrid.Get(this.CellWidth, this.CellHeight, this.CellCountY);

        this.cellCacheMode = new ChunkVisitor(this, InterpolationOrder.CellCache);
        this.fillLoopMode = new ChunkVisitor(this, InterpolationOrder.FillLoop);
    }

    /// <summary>
    /// A noise chunk covering only the cell holding the block column (<paramref name="x"/>, <paramref name="z"/>), like the
    /// one vanilla's <c>iterateNoiseColumn</c> builds.
    /// </summary>
    public static NoiseChunk ForColumn(RandomState randomState, int x, int z)
    {
        var cellWidth = randomState.Settings.Noise.SizeHorizontal * 4;
        return new NoiseChunk(randomState, Mth.FloorDiv(x, cellWidth) * cellWidth, Mth.FloorDiv(z, cellWidth) * cellWidth, 1);
    }

    /// <summary>
    /// Evaluates <see cref="FinalDensity"/> at every block of a cell. The values are the same as calling
    /// <see cref="IDensityFunction.GetValue"/> for each block, but computing them together is much faster.
    /// </summary>
    /// <param name="cellX">Cell index along X from the chunk's first cell, below <see cref="CellCountXZ"/>.</param>
    /// <param name="cellY">Cell index along Y from the lowest cell, below <see cref="CellCountY"/>.</param>
    /// <param name="cellZ">Cell index along Z from the chunk's first cell, below <see cref="CellCountXZ"/>.</param>
    /// <param name="values">Receives <see cref="CellSize"/> values, the block at offset (x, y, z) in the cell at index
    /// <c>(y * CellWidth + z) * CellWidth + x</c>.</param>
    public void FillFinalDensity(int cellX, int cellY, int cellZ, Span<double> values) =>
        (this.finalDensityFiller ??= this.Compile(this.FinalDensity)).Fill(new Cell(this, cellX, cellY, cellZ), values);

    /// <summary>
    /// Evaluates <see cref="VeinToggle"/> at every block of a cell, like <see cref="FillFinalDensity"/>.
    /// </summary>
    public void FillVeinToggle(int cellX, int cellY, int cellZ, Span<double> values) =>
        (this.veinToggleFiller ??= this.Compile(this.VeinToggle)).Fill(new Cell(this, cellX, cellY, cellZ), values);

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

    private int CornerIndex(int cellX, int cellY, int cellZ) => cellX * this.cornerStrideX + cellZ * this.cornerStrideZ + cellY;

    private CellFiller Compile(IDensityFunction function) => function switch
    {
        CellInterpolator interpolator => new InterpolatorFiller(interpolator),
        ConstantDensityFunction constant => new ConstantFiller(constant.Argument),
        AddDensityFunction add => new BinaryFiller(BinaryOperation.Add, this.Compile(add.Argument1), this.Compile(add.Argument2),
            add.Argument2, 0.0, this.CellSize),
        MulDensityFunction mul => new BinaryFiller(BinaryOperation.Mul, this.Compile(mul.Argument1), this.Compile(mul.Argument2),
            mul.Argument2, 0.0, this.CellSize),
        MinDensityFunction min => new BinaryFiller(BinaryOperation.Min, this.Compile(min.Argument1), this.Compile(min.Argument2),
            min.Argument2, min.Argument2.MinValue, this.CellSize),
        MaxDensityFunction max => new BinaryFiller(BinaryOperation.Max, this.Compile(max.Argument1), this.Compile(max.Argument2),
            max.Argument2, max.Argument2.MaxValue, this.CellSize),
        AbsDensityFunction abs => new UnaryFiller(UnaryOperation.Abs, this.Compile(abs.Argument)),
        SquareDensityFunction square => new UnaryFiller(UnaryOperation.Square, this.Compile(square.Argument)),
        CubeDensityFunction cube => new UnaryFiller(UnaryOperation.Cube, this.Compile(cube.Argument)),
        HalfNegativeDensityFunction halfNegative => new UnaryFiller(UnaryOperation.HalfNegative, this.Compile(halfNegative.Argument)),
        QuarterNegativeDensityFunction quarterNegative => new UnaryFiller(UnaryOperation.QuarterNegative, this.Compile(quarterNegative.Argument)),
        SqueezeDensityFunction squeeze => new UnaryFiller(UnaryOperation.Squeeze, this.Compile(squeeze.Argument)),
        InvertDensityFunction invert => new UnaryFiller(UnaryOperation.Invert, this.Compile(invert.Argument)),
        ClampDensityFunction clamp => new UnaryFiller(UnaryOperation.Clamp, this.Compile(clamp.Input), clamp.Min, clamp.Max),
        RangeChoiceDensityFunction rangeChoice => new RangeChoiceFiller(rangeChoice, this.Compile(rangeChoice.Input),
            this.Compile(rangeChoice.WhenInRange), this.Compile(rangeChoice.WhenOutOfRange), this.CellSize),
        BlendDensityFunction blend => this.Compile(blend.Argument),
        CacheOnce cacheOnce => this.Compile(cacheOnce.Argument),
        _ => new PointFiller(function)
    };

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

    private static double Lerp(double delta, double start, double end) => start + delta * (end - start);

    /// <summary>
    /// Lookup tables from block offsets to cells, shared by every chunk with the same cell layout. They replace
    /// divisions by the cell size, which isn't a constant.
    /// </summary>
    private sealed class CellGrid
    {
        private static readonly ConcurrentDictionary<(int Width, int Height, int CountY), CellGrid> grids = new();

        /// <summary>
        /// Cell index and offset in the cell of a block offset along X or Z (up to 16).
        /// </summary>
        public int[] CellOfXZ { get; }

        /// <inheritdoc cref="CellOfXZ"/>
        public int[] InCellXZ { get; }

        /// <summary>
        /// Cell index and offset in the cell of a block offset from the lowest cell along Y.
        /// </summary>
        public int[] CellOfY { get; }

        /// <inheritdoc cref="CellOfY"/>
        public int[] InCellY { get; }

        /// <summary>
        /// Interpolation factor of an offset in the cell, computed like vanilla (<c>(double)offset / size</c>).
        /// </summary>
        public double[] DeltaXZ { get; }

        /// <inheritdoc cref="DeltaXZ"/>
        public double[] DeltaY { get; }

        private CellGrid(int width, int height, int countY)
        {
            (this.CellOfXZ, this.InCellXZ) = Divide(16, width);
            (this.CellOfY, this.InCellY) = Divide(countY * height, height);
            this.DeltaXZ = Deltas(width);
            this.DeltaY = Deltas(height);
        }

        public static CellGrid Get(int width, int height, int countY) =>
            grids.GetOrAdd((width, height, countY), static key => new CellGrid(key.Width, key.Height, key.CountY));

        private static (int[] Quotients, int[] Remainders) Divide(int count, int divisor)
        {
            var quotients = new int[count];
            var remainders = new int[count];

            for (var i = 0; i < count; i++)
                (quotients[i], remainders[i]) = Math.DivRem(i, divisor);

            return (quotients, remainders);
        }

        private static double[] Deltas(int size)
        {
            var deltas = new double[size];

            for (var i = 0; i < size; i++)
                deltas[i] = (double)i / size;

            return deltas;
        }
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
                // Interpolators are told which router function they stand for, which keys the shared corners.
                result = function is InterpolatedDensityFunction interpolated
                    ? new CellInterpolator(this.chunk, this.Map(interpolated.Argument), this.order, interpolated)
                    : function.MapAll(this);

                if (this.chunk.RandomState.SharedFunctions.Contains(function))
                    result = new CacheOnce(result);

                this.mapped[function] = result;
            }

            return result;
        }

        public IDensityFunction Apply(IDensityFunction function) => function switch
        {
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
        private readonly InterpolatedDensityFunction routerFunction;
        private double[]? corners;

        public string Type => "minecraft:interpolated";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        // Corner values, column by column (see CornerIndex).
        private double[] Corners => this.corners ??= this.SampleCorners();

        /// <param name="routerFunction">The router's function this one stands for.</param>
        public CellInterpolator(NoiseChunk chunk, IDensityFunction argument, InterpolationOrder order, InterpolatedDensityFunction routerFunction)
        {
            this.chunk = chunk;
            this.argument = argument;
            this.order = order;
            this.routerFunction = routerFunction;
        }

        public double GetValue(double x, double y, double z)
        {
            var chunk = this.chunk;
            var localX = (int)x - chunk.ChunkMinX;
            var localZ = (int)z - chunk.ChunkMinZ;
            var localY = (int)y - chunk.filledMinY;
            var filledWidth = (uint)chunk.FilledWidth;

            if ((uint)localX >= filledWidth || (uint)localZ >= filledWidth || (uint)localY >= (uint)chunk.filledHeight)
                return this.argument.GetValue(x, y, z);

            var grid = chunk.grid;
            var corners = this.Corners;
            var index = chunk.CornerIndex(grid.CellOfXZ[localX], grid.CellOfY[localY], grid.CellOfXZ[localZ]);
            var strideX = chunk.cornerStrideX;
            var strideZ = chunk.cornerStrideZ;

            var n000 = corners[index];
            var n010 = corners[index + 1];
            var n001 = corners[index + strideZ];
            var n011 = corners[index + strideZ + 1];
            var n100 = corners[index + strideX];
            var n110 = corners[index + strideX + 1];
            var n101 = corners[index + strideX + strideZ];
            var n111 = corners[index + strideX + strideZ + 1];

            var deltaX = grid.DeltaXZ[grid.InCellXZ[localX]];
            var deltaY = grid.DeltaY[grid.InCellY[localY]];
            var deltaZ = grid.DeltaXZ[grid.InCellXZ[localZ]];

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

        /// <summary>
        /// Interpolates every block of a cell, with the same arithmetic as <see cref="GetValue"/>. Partial lerps that
        /// don't depend on Z are shared by the blocks of a row.
        /// </summary>
        public void FillCell(in Cell cell, Span<double> values)
        {
            var chunk = this.chunk;
            var corners = this.Corners;
            var index = chunk.CornerIndex(cell.X, cell.Y, cell.Z);
            var strideX = chunk.cornerStrideX;
            var strideZ = chunk.cornerStrideZ;

            var n000 = corners[index];
            var n010 = corners[index + 1];
            var n001 = corners[index + strideZ];
            var n011 = corners[index + strideZ + 1];
            var n100 = corners[index + strideX];
            var n110 = corners[index + strideX + 1];
            var n101 = corners[index + strideX + strideZ];
            var n111 = corners[index + strideX + strideZ + 1];

            var width = chunk.CellWidth;
            var deltasXZ = chunk.grid.DeltaXZ;
            var deltasY = chunk.grid.DeltaY;
            Span<double> low = stackalloc double[width];
            Span<double> high = stackalloc double[width];
            var i = 0;

            if (this.order == InterpolationOrder.CellCache)
            {
                // Lerp(dZ, Lerp(dY, Lerp(dX, n000, n100), Lerp(dX, n010, n110)), Lerp(dY, Lerp(dX, n001, n101), Lerp(dX, n011, n111)))
                Span<double> x00 = stackalloc double[width];
                Span<double> x10 = stackalloc double[width];
                Span<double> x01 = stackalloc double[width];
                Span<double> x11 = stackalloc double[width];

                for (var x = 0; x < width; x++)
                {
                    var deltaX = deltasXZ[x];
                    x00[x] = Lerp(deltaX, n000, n100);
                    x10[x] = Lerp(deltaX, n010, n110);
                    x01[x] = Lerp(deltaX, n001, n101);
                    x11[x] = Lerp(deltaX, n011, n111);
                }

                for (var y = 0; y < chunk.CellHeight; y++)
                {
                    var deltaY = deltasY[y];

                    for (var x = 0; x < width; x++)
                    {
                        low[x] = Lerp(deltaY, x00[x], x10[x]);
                        high[x] = Lerp(deltaY, x01[x], x11[x]);
                    }

                    for (var z = 0; z < width; z++)
                    {
                        var deltaZ = deltasXZ[z];

                        for (var x = 0; x < width; x++)
                            values[i++] = Lerp(deltaZ, low[x], high[x]);
                    }
                }

                return;
            }

            // Lerp(dZ, Lerp(dX, Lerp(dY, n000, n010), Lerp(dY, n100, n110)), Lerp(dX, Lerp(dY, n001, n011), Lerp(dY, n101, n111)))
            for (var y = 0; y < chunk.CellHeight; y++)
            {
                var deltaY = deltasY[y];
                var y00 = Lerp(deltaY, n000, n010);
                var y10 = Lerp(deltaY, n100, n110);
                var y01 = Lerp(deltaY, n001, n011);
                var y11 = Lerp(deltaY, n101, n111);

                for (var x = 0; x < width; x++)
                {
                    var deltaX = deltasXZ[x];
                    low[x] = Lerp(deltaX, y00, y10);
                    high[x] = Lerp(deltaX, y01, y11);
                }

                for (var z = 0; z < width; z++)
                {
                    var deltaZ = deltasXZ[z];

                    for (var x = 0; x < width; x++)
                        values[i++] = Lerp(deltaZ, low[x], high[x]);
                }
            }
        }

        private double[] SampleCorners()
        {
            var chunk = this.chunk;
            var corners = new double[(chunk.CellCountXZ + 1) * chunk.cornerStrideX];
            var shared = chunk.RandomState.CornerColumns;

            for (var cellX = 0; cellX <= chunk.CellCountXZ; cellX++)
            {
                var columnX = chunk.FirstCellX + cellX;
                var blockX = columnX * chunk.CellWidth;

                for (var cellZ = 0; cellZ <= chunk.CellCountXZ; cellZ++)
                {
                    var columnZ = chunk.FirstCellZ + cellZ;
                    var blockZ = columnZ * chunk.CellWidth;
                    var column = corners.AsSpan(chunk.CornerIndex(cellX, 0, cellZ), chunk.cornerStrideZ);

                    // Border columns are also the neighboring chunks' borders.
                    var border = cellX == 0 || cellZ == 0 || cellX == chunk.CellCountXZ || cellZ == chunk.CellCountXZ;
                    if (border && shared.TryGet(this.routerFunction, columnX, columnZ, column))
                        continue;

                    for (var cellY = 0; cellY <= chunk.CellCountY; cellY++)
                    {
                        var blockY = (chunk.CellNoiseMinY + cellY) * chunk.CellHeight;
                        column[cellY] = this.argument.GetValue(blockX, blockY, blockZ);
                    }

                    if (border)
                        shared.Add(this.routerFunction, columnX, columnZ, column);
                }
            }

            return corners;
        }
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

        // Positions outside the chunk are sampled directly; splines ask for the same one several times in a row (the
        // aquifers' and surface rules' preliminary surface levels look around the chunk).
        private readonly CacheOnce outside;

        public string Type => "minecraft:flat_cache";

        public double MinValue => this.argument.MinValue;

        public double MaxValue => this.argument.MaxValue;

        public FlatCache(NoiseChunk chunk, IDensityFunction argument)
        {
            this.chunk = chunk;
            this.argument = argument;
            this.outside = new CacheOnce(argument);

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
                : this.outside.GetValue(x, y, z);
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
    /// The cell a <see cref="CellFiller"/> fills: its index and its lowest block.
    /// </summary>
    private readonly struct Cell(NoiseChunk chunk, int x, int y, int z)
    {
        public int X { get; } = x;

        public int Y { get; } = y;

        public int Z { get; } = z;

        public int MinBlockX { get; } = chunk.ChunkMinX + x * chunk.CellWidth;

        public int MinBlockY { get; } = chunk.filledMinY + y * chunk.CellHeight;

        public int MinBlockZ { get; } = chunk.ChunkMinZ + z * chunk.CellWidth;

        public int Width { get; } = chunk.CellWidth;

        public int Height { get; } = chunk.CellHeight;
    }

    /// <summary>
    /// A chunk-bound density function compiled to evaluate every block of a cell at once, which shares interpolation
    /// work between blocks and makes a virtual call per node and cell rather than per node and block.
    /// </summary>
    /// <remarks>
    /// The values are the same as <see cref="IDensityFunction.GetValue"/> at each block: every node does the same
    /// arithmetic in the same order. Arguments a node skips for some blocks (like a multiplication's second argument
    /// when the first is zero) are still computed for every block when that's cheap, i.e. no noise is involved;
    /// otherwise they're sampled per block where needed.
    /// </remarks>
    private abstract class CellFiller
    {
        /// <summary>
        /// Whether filling a whole cell costs about as much as a few arithmetic operations per block.
        /// </summary>
        public abstract bool IsCheap { get; }

        /// <summary>
        /// Writes the value of every block of the cell, indexed like <see cref="FillFinalDensity"/>.
        /// </summary>
        public abstract void Fill(in Cell cell, Span<double> values);
    }

    private sealed class ConstantFiller(double value) : CellFiller
    {
        public override bool IsCheap => true;

        public override void Fill(in Cell cell, Span<double> values) => values.Fill(value);
    }

    private sealed class InterpolatorFiller(CellInterpolator interpolator) : CellFiller
    {
        public override bool IsCheap => true;

        public override void Fill(in Cell cell, Span<double> values) => interpolator.FillCell(cell, values);
    }

    /// <summary>
    /// Any other function, sampled block by block.
    /// </summary>
    private sealed class PointFiller(IDensityFunction function) : CellFiller
    {
        public override bool IsCheap { get; } = function is YClampedGradientDensityFunction or BlendAlphaDensityFunction
            or BlendOffsetDensityFunction;

        public override void Fill(in Cell cell, Span<double> values)
        {
            var i = 0;

            for (var y = cell.MinBlockY; y < cell.MinBlockY + cell.Height; y++)
            {
                for (var z = cell.MinBlockZ; z < cell.MinBlockZ + cell.Width; z++)
                {
                    for (var x = cell.MinBlockX; x < cell.MinBlockX + cell.Width; x++)
                        values[i++] = function.GetValue(x, y, z);
                }
            }
        }
    }

    private enum UnaryOperation
    {
        Abs,
        Square,
        Cube,
        HalfNegative,
        QuarterNegative,
        Squeeze,
        Invert,
        Clamp
    }

    private sealed class UnaryFiller(UnaryOperation operation, CellFiller argument, double min = 0.0, double max = 0.0) : CellFiller
    {
        public override bool IsCheap => argument.IsCheap;

        public override void Fill(in Cell cell, Span<double> values)
        {
            argument.Fill(cell, values);

            switch (operation)
            {
                case UnaryOperation.Abs:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = Math.Abs(values[i]);
                    break;
                case UnaryOperation.Square:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = SquareDensityFunction.Square(values[i]);
                    break;
                case UnaryOperation.Cube:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = CubeDensityFunction.Cube(values[i]);
                    break;
                case UnaryOperation.HalfNegative:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = HalfNegativeDensityFunction.Transform(values[i]);
                    break;
                case UnaryOperation.QuarterNegative:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = QuarterNegativeDensityFunction.Transform(values[i]);
                    break;
                case UnaryOperation.Squeeze:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = SqueezeDensityFunction.Transform(values[i]);
                    break;
                case UnaryOperation.Invert:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = 1.0 / values[i];
                    break;
                case UnaryOperation.Clamp:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = Math.Clamp(values[i], min, max);
                    break;
            }
        }
    }

    private enum BinaryOperation
    {
        Add,
        Mul,
        Min,
        Max
    }

    /// <param name="bound">The second argument's minimum for <see cref="BinaryOperation.Min"/> and maximum for
    /// <see cref="BinaryOperation.Max"/>, which decide when the functions skip it.</param>
    private sealed class BinaryFiller(BinaryOperation operation, CellFiller first, CellFiller second, IDensityFunction secondFunction,
        double bound, int cellSize) : CellFiller
    {
        private readonly double[] secondValues = new double[cellSize];

        public override bool IsCheap => first.IsCheap && second.IsCheap;

        public override void Fill(in Cell cell, Span<double> values)
        {
            first.Fill(cell, values);

            if (operation != BinaryOperation.Add && !second.IsCheap)
            {
                this.FillSkipping(cell, values);
                return;
            }

            var secondValues = this.secondValues.AsSpan(0, values.Length);
            second.Fill(cell, secondValues);

            switch (operation)
            {
                case BinaryOperation.Add:
                    for (var i = 0; i < values.Length; i++)
                        values[i] += secondValues[i];
                    break;
                case BinaryOperation.Mul:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = values[i] == 0.0 ? 0.0 : values[i] * secondValues[i];
                    break;
                case BinaryOperation.Min:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = values[i] < bound ? values[i] : Math.Min(values[i], secondValues[i]);
                    break;
                case BinaryOperation.Max:
                    for (var i = 0; i < values.Length; i++)
                        values[i] = values[i] > bound ? values[i] : Math.Max(values[i], secondValues[i]);
                    break;
            }
        }

        // Samples the second argument only at the blocks that need it, like the function itself does.
        private void FillSkipping(in Cell cell, Span<double> values)
        {
            var i = 0;

            for (var y = cell.MinBlockY; y < cell.MinBlockY + cell.Height; y++)
            {
                for (var z = cell.MinBlockZ; z < cell.MinBlockZ + cell.Width; z++)
                {
                    for (var x = cell.MinBlockX; x < cell.MinBlockX + cell.Width; x++, i++)
                    {
                        var value = values[i];
                        values[i] = operation switch
                        {
                            BinaryOperation.Mul => value == 0.0 ? 0.0 : value * secondFunction.GetValue(x, y, z),
                            BinaryOperation.Min => value < bound ? value : Math.Min(value, secondFunction.GetValue(x, y, z)),
                            _ => value > bound ? value : Math.Max(value, secondFunction.GetValue(x, y, z))
                        };
                    }
                }
            }
        }
    }

    private sealed class RangeChoiceFiller(RangeChoiceDensityFunction function, CellFiller input, CellFiller whenInRange,
        CellFiller whenOutOfRange, int cellSize) : CellFiller
    {
        private readonly double[] inputValues = new double[cellSize];
        private readonly double[] outOfRangeValues = new double[cellSize];
        private readonly double minInclusive = function.MinInclusive;
        private readonly double maxExclusive = function.MaxExclusive;

        public override bool IsCheap => input.IsCheap && whenInRange.IsCheap && whenOutOfRange.IsCheap;

        public override void Fill(in Cell cell, Span<double> values)
        {
            var inputValues = this.inputValues.AsSpan(0, values.Length);
            input.Fill(cell, inputValues);

            if (!whenInRange.IsCheap || !whenOutOfRange.IsCheap)
            {
                this.FillPerBlock(cell, inputValues, values);
                return;
            }

            var inRangeCount = 0;
            foreach (var control in inputValues)
            {
                if (control >= this.minInclusive && control < this.maxExclusive)
                    inRangeCount++;
            }

            if (inRangeCount == values.Length)
            {
                whenInRange.Fill(cell, values);
                return;
            }

            if (inRangeCount == 0)
            {
                whenOutOfRange.Fill(cell, values);
                return;
            }

            var outOfRangeValues = this.outOfRangeValues.AsSpan(0, values.Length);
            whenInRange.Fill(cell, values);
            whenOutOfRange.Fill(cell, outOfRangeValues);

            for (var i = 0; i < values.Length; i++)
            {
                if (!(inputValues[i] >= this.minInclusive && inputValues[i] < this.maxExclusive))
                    values[i] = outOfRangeValues[i];
            }
        }

        private void FillPerBlock(in Cell cell, ReadOnlySpan<double> inputValues, Span<double> values)
        {
            var i = 0;

            for (var y = cell.MinBlockY; y < cell.MinBlockY + cell.Height; y++)
            {
                for (var z = cell.MinBlockZ; z < cell.MinBlockZ + cell.Width; z++)
                {
                    for (var x = cell.MinBlockX; x < cell.MinBlockX + cell.Width; x++, i++)
                    {
                        values[i] = inputValues[i] >= this.minInclusive && inputValues[i] < this.maxExclusive
                            ? function.WhenInRange.GetValue(x, y, z)
                            : function.WhenOutOfRange.GetValue(x, y, z);
                    }
                }
            }
        }
    }
}
