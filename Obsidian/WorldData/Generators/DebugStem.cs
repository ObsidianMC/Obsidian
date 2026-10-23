using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Lighting;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// A level stem generating with vanilla's <c>minecraft:debug</c> generator: every block state in a grid (see
/// <see cref="DebugLevelGenerator"/>), in plains.
/// </summary>
/// <param name="identifier">The level stem's identifier.</param>
/// <param name="dimensionType">A registered dimension type.</param>
internal sealed class DebugStem(string identifier, string dimensionType) : LevelStem(identifier, dimensionType)
{
    public override IReadOnlyList<BiomeCodec> SourceBiomes => [CodecRegistry.Biomes.Plains];

    public override IStemGenerator CreateGenerator() => new DebugLevelGenerator(this);
}

/// <summary>
/// Generates vanilla's debug world: a barrier floor at y 60 and, at y 70, every block state, vanilla's and then
/// plugins', each alone in a cell of 2 by 2 blocks, in plains.
/// </summary>
/// <remarks>
/// The layout is vanilla's DebugLevelSource's, so a state is where vanilla clients and players expect it: states are
/// in state id order, which is vanilla's order of blocks and of their states, and fill the grid from (1, 1) by rows
/// along z, at odd x and z. Plugins' blocks follow vanilla's, as their states' ids do; the states are counted when the
/// level starts, so blocks plugins register later join the grid after a restart.
/// </remarks>
internal sealed class DebugLevelGenerator : IStemGenerator
{
    // Vanilla's DebugLevelSource.BARRIER_HEIGHT and HEIGHT.
    private const int BarrierY = 60;
    private const int StatesY = 70;

    private readonly DebugStem stem;

    private ILevel? level;
    private int stateCount;
    private int gridWidth;
    private int gridHeight;

    /// <param name="stem">The level stem the level is generated for.</param>
    public DebugLevelGenerator(DebugStem stem)
    {
        this.stem = stem;
        this.CountStates();
    }

    public string Id => this.stem.Identifier;

    public LevelStem? Taken { get; private set; }

    public void Init(ILevel level)
    {
        this.level = level;
        this.CountStates();

        // The level generates as the files define the stem when it starts (see PluginDimensions.Take).
        this.Taken = PluginDimensions.Take(this.stem) as DebugStem;
    }

    /// <summary>
    /// The block state vanilla's DebugLevelSource.getBlockStateFor places at a column's top, or air.
    /// </summary>
    public IBlock GetBlockAt(int x, int z)
    {
        if (x <= 0 || z <= 0 || x % 2 == 0 || z % 2 == 0)
            return BlocksRegistry.Air;

        x /= 2;
        z /= 2;

        if (x > this.gridWidth || z > this.gridHeight)
            return BlocksRegistry.Air;

        var index = Math.Abs(x * this.gridWidth + z);

        return index < this.stateCount ? BlocksRegistry.Get(index) : BlocksRegistry.Air;
    }

    public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        if (chunk is { IsGenerated: true })
            return chunk;

        var range = this.stem.DimensionType.Element;
        var result = new Chunk(x, z, chunk?.MinY ?? range.MinY, chunk?.Height ?? range.Height);
        var maxY = result.MinY + result.Height;

        for (var bx = 0; bx < 16; bx++)
        {
            for (var bz = 0; bz < 16; bz++)
            {
                // Vanilla's level drops blocks set outside the build range.
                if (BarrierY >= result.MinY && BarrierY < maxY)
                    result.SetBlock(bx, BarrierY, bz, BlocksRegistry.Barrier);

                if (StatesY >= result.MinY && StatesY < maxY)
                    result.SetBlock(bx, StatesY, bz, this.GetBlockAt(x * 16 + bx, z * 16 + bz));
            }
        }

        for (var bx = 0; bx < 16; bx += 4)
        {
            for (var bz = 0; bz < 16; bz += 4)
            {
                for (var y = result.MinY; y < maxY; y += 4)
                    result.SetBiome(bx, y, bz, CodecRegistry.Biomes.Plains);
            }
        }

        WorldgenHeightmaps.UpdateFinal(result, result.MinY, result.Height);

        if (this.level is not null)
            await LightEngine.LightChunkAsync(result, this.level, range.HasSkylight);

        result.SetChunkStatus(ChunkGenStage.full);

        return result;
    }

    /// <summary>
    /// Players spawn where vanilla spawns them in a debug world, above the grid's corner.
    /// </summary>
    public ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(new VectorD(0.5, 80, 0.5));

    // Vanilla's grid: GRID_WIDTH is the ceiling of the state count's square root, in floats, and GRID_HEIGHT the
    // ceiling of the count divided by it. They decide where each state is, so they must match.
    private void CountStates()
    {
        this.stateCount = BlocksRegistry.StateCount;
        this.gridWidth = (int)MathF.Ceiling(MathF.Sqrt(this.stateCount));
        this.gridHeight = (int)MathF.Ceiling((float)this.stateCount / this.gridWidth);
    }
}
