using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Fills a chunk with the default block, fluids and ore veins from the noise router's final density.
/// Mirrors the noise step of vanilla's NoiseBasedChunkGenerator.
/// </summary>
internal sealed class TerrainGenerator : IStructureTerrain
{
    private readonly RandomState randomState;
    private readonly FluidPicker fluidPicker;
    private readonly IBlock defaultBlock;

    public TerrainGenerator(RandomState randomState)
    {
        this.randomState = randomState;

        var settings = randomState.Settings;
        this.defaultBlock = BlocksRegistry.GetFromSimpleState(settings.DefaultBlock);
        this.fluidPicker = Aquifers.CreateGlobalFluidPicker(settings.SeaLevel, BlocksRegistry.GetFromSimpleState(settings.DefaultFluid));
    }

    public int SeaLevel => this.randomState.Settings.SeaLevel;

    /// <param name="chunk">Chunk to fill.</param>
    /// <param name="fluidUpdates">Receives fluid positions that need an update to settle (aquifer edges), like
    /// vanilla's post-processing marks.</param>
    /// <param name="beardifier">Density added around nearby structures, or <c>null</c> for none.</param>
    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null, Beardifier? beardifier = null)
    {
        beardifier ??= Beardifier.Empty;
        var settings = this.randomState.Settings;
        var noiseChunk = new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var aquifer = Aquifers.Create(noiseChunk, chunk.X, chunk.Z, this.fluidPicker);

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
        var blocks = new IBlock?[256 * cellHeight];
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

                    var i = 0;
                    for (var inCellY = 0; inCellY < cellHeight; inCellY++)
                    {
                        var y = cellMinY + inCellY;

                        for (var inCellZ = 0; inCellZ < cellWidth; inCellZ++)
                        {
                            var localZ = cellZ * cellWidth + inCellZ;
                            var z = noiseChunk.ChunkMinZ + localZ;

                            for (var inCellX = 0; inCellX < cellWidth; inCellX++, i++)
                            {
                                var localX = cellX * cellWidth + inCellX;
                                var x = noiseChunk.ChunkMinX + localX;
                                // Like vanilla, the beardifier is added to the final density per block, inside the cell cache.
                                var density = densities[i] + beardifier.Compute(x, y, z);

                                var block = aquifer.ComputeSubstance(x, y, z, density)
                                    ?? (hasVeins ? OreVeinifier.Compute(noiseChunk, x, y, z, veinToggles[i]) : null)
                                    ?? this.defaultBlock;

                                var index = (inCellY * 16 + localZ) * 16 + localX;
                                blocks[index] = block.IsAir ? null : block;
                                scheduled[index] = block.IsLiquid && aquifer.ShouldScheduleFluidUpdate;
                            }
                        }
                    }
                }
            }

            for (var inCellY = cellHeight - 1; inCellY >= 0; inCellY--)
            {
                var y = cellMinY + inCellY;
                var layer = blocks.AsSpan(inCellY * 256, 256);
                SetLayer(chunk, y, layer);

                for (var column = 0; column < 256; column++)
                {
                    var block = layer[column];
                    if (block is null)
                        continue;

                    if (fluidUpdates is not null && scheduled[inCellY * 256 + column])
                        fluidUpdates.Add(new Vector(noiseChunk.ChunkMinX + (column & 15), y, noiseChunk.ChunkMinZ + (column >> 4)));

                    worldSurface[column] = Math.Max(worldSurface[column], y + 1);

                    if (oceanFloor[column] < y + 1 && block.BlocksMotion())
                        oceanFloor[column] = y + 1;
                }
            }
        }

        WorldgenHeightmaps.Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        WorldgenHeightmaps.Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
    }

    /// <summary>
    /// Sets the non-null blocks of a layer (indexed <c>z * 16 + x</c>) in index order.
    /// </summary>
    private static void SetLayer(IChunk chunk, int y, ReadOnlySpan<IBlock?> layer)
    {
        if (chunk.Sections[(y - chunk.MinY) >> 4] is ChunkSection section)
        {
            section.SetBlockLayer(y & 15, layer);
            return;
        }

        for (var column = 0; column < 256; column++)
        {
            if (layer[column] is { } block)
                chunk.SetBlock(column & 15, y, column >> 4, block);
        }
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
        var aquifer = Aquifers.Create(noiseChunk, noiseChunk.ChunkMinX >> 4, noiseChunk.ChunkMinZ >> 4, this.fluidPicker);
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
}
