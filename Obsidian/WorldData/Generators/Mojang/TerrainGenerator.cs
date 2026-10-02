using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Fills a chunk with the default block, fluids and ore veins from the noise router's final density.
/// Mirrors the noise step of vanilla's NoiseBasedChunkGenerator.
/// </summary>
internal sealed class TerrainGenerator : IStructureTerrain
{
    private readonly RandomState randomState;
    private readonly IBlock defaultBlock;

    // A fill's buffers, kept by each thread for its next fill. Taken while in use, so a nested fill would get its own.
    [ThreadStatic]
    private static FillBuffers? freeBuffers;

    // See IterateColumn.
    private readonly ThreadLocal<NoiseChunk?> columnNoiseChunks = new();

    public TerrainGenerator(RandomState randomState)
    {
        this.randomState = randomState;
        this.defaultBlock = BlocksRegistry.GetFromSimpleState(randomState.Settings.DefaultBlock);
    }

    public int SeaLevel => this.randomState.Settings.SeaLevel;

    /// <param name="chunk">Chunk to fill.</param>
    /// <param name="fluidUpdates">Receives fluid positions that need an update to settle (aquifer edges), like
    /// vanilla's post-processing marks.</param>
    /// <param name="beardifier">Density added around nearby structures, or <c>null</c> for none.</param>
    /// <param name="noiseChunk">The chunk's noise chunk when the other steps share it, or <c>null</c> for a new one.</param>
    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null, Beardifier? beardifier = null, NoiseChunk? noiseChunk = null)
    {
        beardifier ??= Beardifier.Empty;
        var cellBeardifierBuffer = beardifier == Beardifier.Empty ? null : beardifier.CreateBuffer();
        var settings = this.randomState.Settings;
        noiseChunk ??= new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var aquifer = noiseChunk.Aquifer;
        var globalFluidAboveY = aquifer.GlobalFluidAboveY;
        var globalFluid = this.randomState.GlobalFluidPicker;

        var cellWidth = noiseChunk.CellWidth;
        var cellHeight = noiseChunk.CellHeight;
        var minY = noiseChunk.CellNoiseMinY * cellHeight;

        // Heightmaps hold the first free Y above the highest matching block, like vanilla.
        Span<int> worldSurface = stackalloc int[256];
        Span<int> oceanFloor = stackalloc int[256];
        worldSurface.Fill(minY);
        oceanFloor.Fill(minY);

        // Columns the cells don't cover keep their blocks, which the heightmaps then don't count, like air.
        var openColumns = noiseChunk.FilledWidth * noiseChunk.FilledWidth;

        // Blocks are computed a layer of cells at a time, a cell at once, then written from the top down in z, x order.
        // Writing in that order keeps the order of the sections' palettes and of the fluid updates.
        var buffers = freeBuffers ?? new FillBuffers();
        freeBuffers = null;
        var palette = buffers.Palette;
        palette.Reset(this.defaultBlock);
        var codes = buffers.Codes(256 * cellHeight);
        var scheduled = buffers.Scheduled(256 * cellHeight);
        Span<double> densities = stackalloc double[noiseChunk.CellSize];
        Span<double> veinToggles = stackalloc double[noiseChunk.CellSize];

        for (var cellY = noiseChunk.CellCountY - 1; cellY >= 0; cellY--)
        {
            var cellMinY = minY + cellY * cellHeight;
            var hasVeins = settings.OreVeinsEnabled && cellMinY <= OreVeinifier.MaxY && cellMinY + cellHeight > OreVeinifier.MinY;

            for (var cellZ = 0; cellZ < noiseChunk.CellCountXZ; cellZ++)
            {
                for (var cellX = 0; cellX < noiseChunk.CellCountXZ; cellX++)
                {
                    noiseChunk.FillFinalDensity(cellX, cellY, cellZ, densities);
                    if (hasVeins)
                        noiseChunk.FillVeinToggle(cellX, cellY, cellZ, veinToggles);

                    var minLocalX = cellX * cellWidth;
                    var minLocalZ = cellZ * cellWidth;
                    var cellMinX = noiseChunk.ChunkMinX + minLocalX;
                    var cellMinZ = noiseChunk.ChunkMinZ + minLocalZ;
                    var cellBox = new BlockBox(new Vector(cellMinX, cellMinY, cellMinZ),
                        new Vector(cellMinX + cellWidth - 1, cellMinY + cellHeight - 1, cellMinZ + cellWidth - 1));
                    var cellBeardifier = beardifier.Within(cellBox, cellBeardifierBuffer);
                    var bearded = cellBeardifier != Beardifier.Empty;

                    // A solid cell without veins is all default block, since aquifers leave solid positions alone.
                    if (!bearded && IsSolid(densities) && !(hasVeins && OreVeinifier.MayHaveVeins(veinToggles)))
                    {
                        for (var inCellY = 0; inCellY < cellHeight; inCellY++)
                        {
                            for (var inCellZ = 0; inCellZ < cellWidth; inCellZ++)
                            {
                                var row = (inCellY * 16 + minLocalZ + inCellZ) * 16 + minLocalX;
                                codes.AsSpan(row, cellWidth).Fill(FillPalette.DefaultCode);
                                scheduled.AsSpan(row, cellWidth).Clear();
                            }
                        }

                        continue;
                    }

                    // So is a cell above the aquifers' sampling range without solid blocks all global fluid, which only
                    // depends on Y.
                    if (!bearded && cellMinY > globalFluidAboveY && IsOpen(densities))
                    {
                        for (var inCellY = 0; inCellY < cellHeight; inCellY++)
                        {
                            var y = cellMinY + inCellY;
                            var code = palette.CodeOf(globalFluid(cellMinX, y, cellMinZ).At(y));

                            for (var inCellZ = 0; inCellZ < cellWidth; inCellZ++)
                            {
                                var row = (inCellY * 16 + minLocalZ + inCellZ) * 16 + minLocalX;
                                codes.AsSpan(row, cellWidth).Fill(code);
                                scheduled.AsSpan(row, cellWidth).Clear();
                            }
                        }

                        continue;
                    }

                    var i = 0;
                    for (var inCellY = 0; inCellY < cellHeight; inCellY++)
                    {
                        var y = cellMinY + inCellY;
                        var globalCode = y > globalFluidAboveY ? palette.CodeOf(globalFluid(cellMinX, y, cellMinZ).At(y)) : default;

                        for (var inCellZ = 0; inCellZ < cellWidth; inCellZ++)
                        {
                            var z = cellMinZ + inCellZ;
                            var row = (inCellY * 16 + minLocalZ + inCellZ) * 16 + minLocalX;

                            for (var inCellX = 0; inCellX < cellWidth; inCellX++, i++)
                            {
                                var x = cellMinX + inCellX;
                                // Like vanilla, the beardifier is added to the final density per block, inside the cell cache.
                                var density = densities[i] + (bearded ? cellBeardifier.Compute(x, y, z) : 0.0);

                                // Aquifers leave solid positions alone (and don't schedule them), and above their sampling
                                // range give the rest the global fluid, so neither needs the call.
                                byte code;
                                var scheduleFluidUpdate = false;
                                if (!(density > 0.0) && y > globalFluidAboveY)
                                {
                                    code = globalCode;
                                }
                                else if (!(density > 0.0) && aquifer.ComputeSubstance(x, y, z, density) is { } substance)
                                {
                                    code = palette.CodeOf(substance);
                                    scheduleFluidUpdate = palette.IsLiquid(code) && aquifer.ShouldScheduleFluidUpdate;
                                }
                                else if (hasVeins && OreVeinifier.Compute(noiseChunk, x, y, z, veinToggles[i]) is { } vein)
                                {
                                    code = palette.CodeOf(vein);
                                }
                                else
                                {
                                    code = FillPalette.DefaultCode;
                                }

                                codes[row + inCellX] = code;
                                scheduled[row + inCellX] = scheduleFluidUpdate;
                            }
                        }
                    }
                }
            }

            for (var inCellY = cellHeight - 1; inCellY >= 0; inCellY--)
            {
                var y = cellMinY + inCellY;
                var layer = codes.AsSpan(inCellY * 256, 256);
                palette.SetLayer(chunk, y, layer);

                var layerScheduled = fluidUpdates is not null && scheduled.AsSpan(inCellY * 256, 256).Contains(true);
                if (openColumns == 0 && !layerScheduled)
                    continue;

                for (var column = 0; column < 256; column++)
                {
                    var code = layer[column];
                    if (palette.IsAir(code))
                        continue;

                    if (layerScheduled && scheduled[inCellY * 256 + column])
                        fluidUpdates!.Add(new Vector(noiseChunk.ChunkMinX + (column & 15), y, noiseChunk.ChunkMinZ + (column >> 4)));

                    // Blocks below a column's first motion blocking block (which is at or below its first non-air one) change
                    // neither height.
                    if (oceanFloor[column] != minY)
                        continue;

                    worldSurface[column] = Math.Max(worldSurface[column], y + 1);

                    if (palette.BlocksMotion(code))
                    {
                        oceanFloor[column] = y + 1;
                        openColumns--;
                    }
                }
            }
        }

        WorldgenHeightmaps.Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        WorldgenHeightmaps.Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
        freeBuffers = buffers;
    }

    private static bool IsOpen(ReadOnlySpan<double> densities)
    {
        foreach (var density in densities)
        {
            if (density > 0.0)
                return false;
        }

        return true;
    }

    private static bool IsSolid(ReadOnlySpan<double> densities)
    {
        foreach (var density in densities)
        {
            if (!(density > 0.0))
                return false;
        }

        return true;
    }

    public int GetBaseHeight(int x, int z, HeightmapType heightmap) =>
        this.IterateColumn(x, z, block => WorldgenHeightmaps.Matches(heightmap, block), null) ?? this.randomState.Settings.Noise.MinY;

    public NoiseColumn GetBaseColumn(int x, int z)
    {
        var noise = this.randomState.Settings.Noise;
        var blocks = new IBlock[noise.Height];
        this.IterateColumn(x, z, null, blocks);
        return new NoiseColumn(noise.MinY, blocks);
    }

    /// <summary>
    /// Vanilla <c>NoiseBasedChunkGenerator.iterateNoiseColumn</c>: the noise's blocks of a column from the top down, without
    /// structures.
    /// </summary>
    /// <param name="stopAt">Stops at the first block matching it and returns the Y above it.</param>
    /// <param name="blocks">Receives every block, indexed from the noise's min Y.</param>
    private int? IterateColumn(int x, int z, Predicate<IBlock>? stopAt, IBlock[]? blocks)
    {
        // Each thread keeps its column noise chunk for the next column, which saves mapping the router again, and when the
        // column is in the same cell, sampling its corners again. Taken while in use.
        var noiseChunk = this.columnNoiseChunks.Value;
        this.columnNoiseChunks.Value = null;
        if (noiseChunk is null)
            noiseChunk = NoiseChunk.ForColumn(this.randomState, x, z);
        else
            noiseChunk.MoveToColumn(x, z);

        try
        {
            return this.IterateColumn(noiseChunk, x, z, stopAt, blocks);
        }
        finally
        {
            this.columnNoiseChunks.Value = noiseChunk;
        }
    }

    private int? IterateColumn(NoiseChunk noiseChunk, int x, int z, Predicate<IBlock>? stopAt, IBlock[]? blocks)
    {
        var aquifer = noiseChunk.Aquifer;
        var settings = this.randomState.Settings;
        var minY = noiseChunk.CellNoiseMinY * noiseChunk.CellHeight;

        for (var y = minY + noiseChunk.CellCountY * noiseChunk.CellHeight - 1; y >= minY; y--)
        {
            var density = noiseChunk.FinalDensity.GetValue(x, y, z);
            var block = aquifer.ComputeSubstance(x, y, z, density)
                ?? (settings.OreVeinsEnabled ? OreVeinifier.Compute(noiseChunk, x, y, z) : null)
                ?? this.defaultBlock;

            if (blocks is not null)
                blocks[y - minY] = block;

            if (stopAt is not null && stopAt(block))
                return y + 1;
        }

        return null;
    }
    /// <summary>
    /// The buffers of a fill: its palette, and the codes and fluid update flags of a layer of cells.
    /// </summary>
    private sealed class FillBuffers
    {
        private byte[] codes = [];
        private bool[] scheduled = [];

        public FillPalette Palette { get; } = new();

        /// <summary>
        /// A buffer of at least <paramref name="length"/> codes, all <see cref="FillPalette.SkipCode"/>.
        /// </summary>
        public byte[] Codes(int length)
        {
            if (this.codes.Length < length)
                this.codes = new byte[length];

            this.codes.AsSpan().Fill(FillPalette.SkipCode);
            return this.codes;
        }

        /// <summary>
        /// A cleared buffer of at least <paramref name="length"/> flags.
        /// </summary>
        public bool[] Scheduled(int length)
        {
            if (this.scheduled.Length < length)
                this.scheduled = new bool[length];
            else
                Array.Clear(this.scheduled);

            return this.scheduled;
        }
    }

    /// <summary>
    /// The distinct blocks of one fill, numbered in order of appearance so the fill can store a byte per block. Air is
    /// numbered too, but kept as a null block, which layer writes skip.
    /// </summary>
    private sealed class FillPalette
    {
        /// <summary>
        /// The code of the settings' default block, which most blocks are.
        /// </summary>
        public const byte DefaultCode = 0;

        /// <summary>
        /// The code of positions the fill doesn't compute (with cells that don't divide the chunk's width), which stay
        /// as they are, like air.
        /// </summary>
        public const byte SkipCode = byte.MaxValue;

        // Indexed by code; a code is a byte.
        private readonly IBlock[] keys = new IBlock[256];
        private readonly IBlock?[] blocks = new IBlock?[256];
        private readonly bool[] liquid = new bool[256];
        private readonly bool[] blocksMotion = new bool[256];
        private int count;
        private IBlock? last;
        private byte lastCode;

        /// <summary>
        /// Empties the palette for a new fill, whose default block gets <see cref="DefaultCode"/>.
        /// </summary>
        public void Reset(IBlock defaultBlock)
        {
            this.count = 0;
            this.last = null;
            this.CodeOf(defaultBlock);
        }

        public byte CodeOf(IBlock block)
        {
            if (ReferenceEquals(block, this.last))
                return this.lastCode;

            var code = 0;
            while (code < this.count && !ReferenceEquals(this.keys[code], block))
                code++;

            if (code == this.count)
            {
                if (this.count == SkipCode)
                    throw new InvalidOperationException("Too many distinct blocks in one terrain fill.");

                this.keys[code] = block;
                this.blocks[code] = block.IsAir ? null : block;
                this.liquid[code] = block.IsLiquid;
                this.blocksMotion[code] = block.BlocksMotion();
                this.count++;
            }

            this.last = block;
            this.lastCode = (byte)code;
            return this.lastCode;
        }

        public bool IsAir(byte code) => this.blocks[code] is null;

        public bool IsLiquid(byte code) => this.liquid[code];

        public bool BlocksMotion(byte code) => this.blocksMotion[code];

        /// <summary>
        /// Sets the non-air blocks of a layer (codes indexed <c>z * 16 + x</c>) in index order.
        /// </summary>
        public void SetLayer(IChunk chunk, int y, ReadOnlySpan<byte> layer)
        {
            // Past the palette's codes the blocks are stale, but only the skip code, whose block is null, is used.
            var blocks = this.blocks.AsSpan(0, layer.Contains(SkipCode) ? this.blocks.Length : this.count);

            if (chunk.Sections[(y - chunk.MinY) >> 4] is ChunkSection section)
            {
                section.SetBlockLayer(y & 15, layer, blocks);
                return;
            }

            for (var column = 0; column < 256; column++)
            {
                if (blocks[layer[column]] is { } block)
                    chunk.SetBlock(column & 15, y, column >> 4, block);
            }
        }
    }
}
