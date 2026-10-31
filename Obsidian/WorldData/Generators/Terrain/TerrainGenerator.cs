using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// The 26.3 terrain status, built alongside the live 1.21.11 generator. Biomes and structure density are explicit
/// inputs: their Phase 5 steps can supply them without changing the order of terrain's fill, materials and carving.
/// </summary>
internal sealed class TerrainGenerator
{
    public TerrainGenerator(
        WorldgenData data,
        long seed,
        string settingsId,
        DensityFunction? beardifier = null)
    {
        this.Data = data;
        this.Seed = seed;
        this.Settings = NoiseGeneratorSettings.Read(data, settingsId);
        this.Compiler = new DensityCompiler(data, seed, this.Settings.LegacyRandomSource, beardifier);
        this.Router = DensityRouter.Create(this.Settings, this.Compiler);
        this.DefaultBlock = BlocksRegistry.Get(this.Settings.DefaultBlock);
        this.DefaultFluid = BlocksRegistry.Get(this.Settings.DefaultFluid);
        this.Materials = new MaterialRules(this);
    }

    public WorldgenData Data { get; }

    public long Seed { get; }

    public NoiseGeneratorSettings Settings { get; }

    public DensityCompiler Compiler { get; }

    public DensityRouter Router { get; }

    public IBlock DefaultBlock { get; }

    public IBlock DefaultFluid { get; }

    public MaterialRules Materials { get; }

    /// <summary>
    /// Runs the entire terrain status. The biome lookup uses block coordinates; the carver lookup uses source chunk
    /// coordinates and returns the biome's ordered carver identifiers. Neither lookup may generate terrain recursively.
    /// </summary>
    /// <param name="beardifier">The structures' density around the chunk, added to the final density.</param>
    public void Generate(
        IChunk chunk,
        Func<int, int, int, string> biomeAt,
        Func<int, int, IReadOnlyList<string>> carversAt,
        ICollection<Vector>? fluidUpdates = null,
        Beardifier? beardifier = null)
    {
        var context = this.CreateContext(chunk, biomeAt, fluidUpdates);

        this.Fill(context, beardifier);
        this.Materials.Apply(context);
        TerrainCarvers.Apply(context, carversAt);
    }

    public TerrainContext CreateContext(
        IChunk chunk,
        Func<int, int, int, string> biomeAt,
        ICollection<Vector>? fluidUpdates = null) => new(this, chunk, biomeAt, fluidUpdates);

    /// <summary>Fills a chunk volume using vanilla's volume path and z, x, descending-y write order.</summary>
    /// <remarks>
    /// Every 26.3 noise setting ends its final density with <c>add(..., beardifier)</c>, which the router compiles
    /// to 0, so the structures' density is added here, in float like vanilla's addition.
    /// </remarks>
    public void Fill(TerrainContext context, Beardifier? beardifier = null)
    {
        var chunk = context.Chunk;
        var volume = context.Volume;
        using var density = DensityBuffer.Sample(this.Router.FinalDensity, volume);

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = volume.SizeY - 1; y >= 0; y--)
                {
                    var blockX = volume.BlockX(x);
                    var blockY = volume.BlockY(y);
                    var blockZ = volume.BlockZ(z);
                    var value = density.Span[volume.Index(x, y, z)];

                    if (beardifier is not null)
                    {
                        value += beardifier.Sample(blockX, blockY, blockZ);
                    }

                    var block = context.Aquifer.ComputeSubstance(blockX, blockY, blockZ, value) ?? this.DefaultBlock;

                    if (block.IsAir)
                    {
                        continue;
                    }

                    chunk.SetBlock(x, blockY, z, block);
                    context.ScheduleFluid(blockX, blockY, blockZ, block);
                }
            }
        }

        WorldgenHeightmaps.Update(chunk, volume.MinBlockY, volume.SizeY);
    }
}

/// <summary>Mutable state of one terrain status. It is never shared by concurrently generated chunks.</summary>
internal sealed class TerrainContext
{
    public TerrainContext(
        TerrainGenerator generator,
        IChunk chunk,
        Func<int, int, int, string> biomeAt,
        ICollection<Vector>? fluidUpdates)
    {
        this.Generator = generator;
        this.Chunk = chunk;
        this.BiomeAt = biomeAt;
        this.FluidUpdates = fluidUpdates;

        var minY = Math.Max(chunk.MinY, generator.Settings.MinY);
        var maxY = Math.Min(chunk.MinY + chunk.Height, generator.Settings.MinY + generator.Settings.Height);
        this.Volume = new DensityVolume(16, maxY - minY, 16, chunk.X << 4, minY, chunk.Z << 4);
        this.Aquifer = new TerrainAquifer(this);
    }

    public TerrainGenerator Generator { get; }

    public IChunk Chunk { get; }

    public DensityVolume Volume { get; }

    public Func<int, int, int, string> BiomeAt { get; }

    public ICollection<Vector>? FluidUpdates { get; }

    public TerrainAquifer Aquifer { get; }

    public void ScheduleFluid(int x, int y, int z, IBlock block)
    {
        if (block.IsLiquid && this.Aquifer.ShouldScheduleFluidUpdate)
        {
            this.MarkForPostProcessing(x, y, z);
        }
    }

    public void MarkForPostProcessing(int x, int y, int z) => this.FluidUpdates?.Add(new Vector(x, y, z));
}
