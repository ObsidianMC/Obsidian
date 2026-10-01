namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Fills a chunk with the default block, fluids and ore veins from the noise router's final density.
/// Mirrors the noise step of vanilla's NoiseBasedChunkGenerator.
/// </summary>
internal sealed class TerrainGenerator
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

    /// <param name="chunk">Chunk to fill.</param>
    /// <param name="fluidUpdates">Receives fluid positions that need an update to settle (aquifer edges), like
    /// vanilla's post-processing marks.</param>
    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        var settings = this.randomState.Settings;
        var noiseChunk = new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var aquifer = Aquifers.Create(noiseChunk, chunk.X, chunk.Z, this.fluidPicker);

        var minY = noiseChunk.CellNoiseMinY * noiseChunk.CellHeight;
        var maxY = minY + noiseChunk.CellCountY * noiseChunk.CellHeight;

        // Heightmaps hold the first free Y above the highest matching block, like vanilla.
        Span<int> worldSurface = stackalloc int[256];
        Span<int> oceanFloor = stackalloc int[256];
        worldSurface.Fill(minY);
        oceanFloor.Fill(minY);

        for (var y = maxY - 1; y >= minY; y--)
        {
            for (var localZ = 0; localZ < noiseChunk.FilledWidth; localZ++)
            {
                var z = noiseChunk.ChunkMinZ + localZ;

                for (var localX = 0; localX < noiseChunk.FilledWidth; localX++)
                {
                    var x = noiseChunk.ChunkMinX + localX;
                    var density = noiseChunk.FinalDensity.GetValue(x, y, z);

                    var block = aquifer.ComputeSubstance(x, y, z, density)
                        ?? (settings.OreVeinsEnabled ? OreVeinifier.Compute(noiseChunk, x, y, z) : null)
                        ?? this.defaultBlock;

                    if (block.IsAir)
                        continue;

                    chunk.SetBlock(localX, y, localZ, block);

                    if (fluidUpdates is not null && block.IsLiquid && aquifer.ShouldScheduleFluidUpdate)
                        fluidUpdates.Add(new Vector(x, y, z));

                    var column = localZ * 16 + localX;
                    worldSurface[column] = Math.Max(worldSurface[column], y + 1);

                    if (block.BlocksMotion())
                        oceanFloor[column] = Math.Max(oceanFloor[column], y + 1);
                }
            }
        }

        WorldgenHeightmaps.Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        WorldgenHeightmaps.Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
    }
}
