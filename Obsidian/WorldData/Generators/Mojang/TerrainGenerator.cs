using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using System.Runtime.InteropServices;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Fills a chunk with the default block, fluids and ore veins from the noise router's final density.
/// Mirrors the noise step of vanilla's NoiseBasedChunkGenerator.
/// </summary>
internal sealed class TerrainGenerator : IStructureTerrain
{
    private readonly RandomState randomState;
    private readonly IBlock defaultBlock;

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
        var settings = this.randomState.Settings;
        noiseChunk ??= new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var aquifer = noiseChunk.Aquifer;

        var cellWidth = noiseChunk.CellWidth;
        var cellHeight = noiseChunk.CellHeight;
        var minY = noiseChunk.CellNoiseMinY * cellHeight;

        // Heightmaps hold the first free Y above the highest matching block, like vanilla.
        Span<int> worldSurface = stackalloc int[256];
        Span<int> oceanFloor = stackalloc int[256];
        worldSurface.Fill(minY);
        oceanFloor.Fill(minY);

        // Blocks are computed a layer of cells at a time, a cell at once, then written from the top down in z, x order.
        // Writing in that order keeps the order of the sections' palettes and of the fluid updates.
        var palette = new FillPalette(this.defaultBlock);
        var codes = new byte[256 * cellHeight];
        var scheduled = new bool[256 * cellHeight];
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
                    var cellBeardifier = beardifier.Within(new BlockBox(new Vector(cellMinX, cellMinY, cellMinZ),
                        new Vector(cellMinX + cellWidth - 1, cellMinY + cellHeight - 1, cellMinZ + cellWidth - 1)));
                    var bearded = cellBeardifier != Beardifier.Empty;

                    var i = 0;
                    for (var inCellY = 0; inCellY < cellHeight; inCellY++)
                    {
                        var y = cellMinY + inCellY;

                        for (var inCellZ = 0; inCellZ < cellWidth; inCellZ++)
                        {
                            var z = cellMinZ + inCellZ;
                            var row = (inCellY * 16 + minLocalZ + inCellZ) * 16 + minLocalX;

                            for (var inCellX = 0; inCellX < cellWidth; inCellX++, i++)
                            {
                                var x = cellMinX + inCellX;
                                // Like vanilla, the beardifier is added to the final density per block, inside the cell cache.
                                var density = densities[i] + (bearded ? cellBeardifier.Compute(x, y, z) : 0.0);

                                // Aquifers leave solid positions alone (and don't schedule them), so those skip the call.
                                byte code;
                                var scheduleFluidUpdate = false;
                                if (!(density > 0.0) && aquifer.ComputeSubstance(x, y, z, density) is { } substance)
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

                for (var column = 0; column < 256; column++)
                {
                    var code = layer[column];
                    if (palette.IsAir(code))
                        continue;

                    if (fluidUpdates is not null && scheduled[inCellY * 256 + column])
                        fluidUpdates.Add(new Vector(noiseChunk.ChunkMinX + (column & 15), y, noiseChunk.ChunkMinZ + (column >> 4)));

                    worldSurface[column] = Math.Max(worldSurface[column], y + 1);

                    if (palette.BlocksMotion(code))
                        oceanFloor[column] = Math.Max(oceanFloor[column], y + 1);
                }
            }
        }

        WorldgenHeightmaps.Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        WorldgenHeightmaps.Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
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
        var noiseChunk = NoiseChunk.ForColumn(this.randomState, x, z);
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
    /// The distinct blocks of one fill, numbered in order of appearance so the fill can store a byte per block. Air is
    /// numbered too, but kept as a null block, which layer writes skip.
    /// </summary>
    private sealed class FillPalette
    {
        /// <summary>
        /// The code of the settings' default block, which most blocks are.
        /// </summary>
        public const byte DefaultCode = 0;

        private readonly List<IBlock> keys = [];
        private readonly List<IBlock?> blocks = [];
        private readonly List<(bool IsLiquid, bool BlocksMotion)> flags = [];
        private IBlock? last;
        private byte lastCode;

        public FillPalette(IBlock defaultBlock) => this.CodeOf(defaultBlock);

        public byte CodeOf(IBlock block)
        {
            if (ReferenceEquals(block, this.last))
                return this.lastCode;

            var code = 0;
            while (code < this.keys.Count && !ReferenceEquals(this.keys[code], block))
                code++;

            if (code == this.keys.Count)
            {
                if (this.keys.Count > byte.MaxValue)
                    throw new InvalidOperationException("Too many distinct blocks in one terrain fill.");

                code = this.keys.Count;
                this.keys.Add(block);
                this.blocks.Add(block.IsAir ? null : block);
                this.flags.Add((block.IsLiquid, block.BlocksMotion()));
            }

            this.last = block;
            this.lastCode = (byte)code;
            return this.lastCode;
        }

        public bool IsAir(byte code) => this.blocks[code] is null;

        public bool IsLiquid(byte code) => this.flags[code].IsLiquid;

        public bool BlocksMotion(byte code) => this.flags[code].BlocksMotion;

        /// <summary>
        /// Sets the non-air blocks of a layer (codes indexed <c>z * 16 + x</c>) in index order.
        /// </summary>
        public void SetLayer(IChunk chunk, int y, ReadOnlySpan<byte> layer)
        {
            var blocks = CollectionsMarshal.AsSpan(this.blocks);

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
