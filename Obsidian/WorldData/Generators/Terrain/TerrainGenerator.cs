using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
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
        : this(data, seed, NoiseGeneratorSettings.Read(data, settingsId), beardifier)
    {
    }

    /// <param name="settings">The noise settings, read from <paramref name="data"/> or defined inline.</param>
    /// <param name="types">The density function types beyond vanilla's the settings may name, such as plugins'.
    /// </param>
    public TerrainGenerator(
        WorldgenData data,
        long seed,
        NoiseGeneratorSettings settings,
        DensityFunction? beardifier = null,
        IDensityTypes? types = null)
    {
        this.Data = data;
        this.Seed = seed;
        this.Settings = settings;
        this.Compiler = new DensityCompiler(data, seed, this.Settings.LegacyRandomSource, beardifier, types);
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
    /// Runs the entire terrain status. The biome lookups use block coordinates; the carver lookup uses source chunk
    /// coordinates and returns the biome's ordered carver identifiers. No lookup may generate terrain recursively.
    /// </summary>
    /// <param name="beardifier">The structures' density around the chunk, added to the final density.</param>
    public void Generate(
        IChunk chunk,
        TerrainBiomes biomes,
        Func<int, int, IReadOnlyList<string>> carversAt,
        ICollection<Vector>? fluidUpdates = null,
        Beardifier? beardifier = null)
    {
        var context = this.CreateContext(chunk, biomes, fluidUpdates);

        this.Fill(context, beardifier);
        this.Materials.Apply(context);
        TerrainCarvers.Apply(context, carversAt);
    }

    /// <summary>Runs the entire terrain status, with one biome lookup for both the surface and carving.</summary>
    /// <param name="biomeAt">The biomes both the surface and the carvers read.</param>
    public void Generate(
        IChunk chunk,
        Func<int, int, int, string> biomeAt,
        Func<int, int, IReadOnlyList<string>> carversAt,
        ICollection<Vector>? fluidUpdates = null,
        Beardifier? beardifier = null) =>
        this.Generate(chunk, new TerrainBiomes(biomeAt, biomeAt), carversAt, fluidUpdates, beardifier);

    public TerrainContext CreateContext(
        IChunk chunk,
        TerrainBiomes biomes,
        ICollection<Vector>? fluidUpdates = null) => new(this, chunk, biomes, fluidUpdates);

    /// <param name="biomeAt">The biomes both the surface and the carvers read.</param>
    public TerrainContext CreateContext(
        IChunk chunk,
        Func<int, int, int, string> biomeAt,
        ICollection<Vector>? fluidUpdates = null) =>
        this.CreateContext(chunk, new TerrainBiomes(biomeAt, biomeAt), fluidUpdates);

    /// <summary>Fills a chunk volume using vanilla's volume path and z, x, descending-y write order.</summary>
    /// <param name="beardifier">The structures' density around the chunk, added to the final density.</param>
    public void Fill(TerrainContext context, Beardifier? beardifier = null) =>
        TerrainFill.Run(context, beardifier);
}

/// <summary>The block biomes one chunk's terrain status reads, by block coordinates.</summary>
/// <param name="Surface">
/// What material rules read for the surface: vanilla zooms over the biomes stored in the chunk and its neighbors.
/// </param>
/// <param name="Carving">
/// What material rules read for the top blocks carving exposes: vanilla zooms over uncached climate searches there.
/// </param>
internal readonly record struct TerrainBiomes(Func<int, int, int, string> Surface, Func<int, int, int, string> Carving)
{
    /// <summary>
    /// Every biome <see cref="Surface"/> can return for the chunk's blocks, when known; <c>null</c> otherwise. Biome
    /// conditions naming none of them are false without a lookup.
    /// </summary>
    public IReadOnlyList<string>? SurfaceNames { get; init; }
}

/// <summary>Mutable state of one terrain status. It is never shared by concurrently generated chunks.</summary>
internal sealed class TerrainContext
{
    public TerrainContext(
        TerrainGenerator generator,
        IChunk chunk,
        TerrainBiomes biomes,
        ICollection<Vector>? fluidUpdates)
    {
        this.Generator = generator;
        this.Chunk = chunk;
        this.Biomes = biomes;
        this.FluidUpdates = fluidUpdates;

        var minY = Math.Max(chunk.MinY, generator.Settings.MinY);
        var maxY = Math.Min(chunk.MinY + chunk.Height, generator.Settings.MinY + generator.Settings.Height);
        this.Volume = new DensityVolume(16, maxY - minY, 16, chunk.X << 4, minY, chunk.Z << 4);
        this.Aquifer = new TerrainAquifer(this);
    }

    public TerrainGenerator Generator { get; }

    public IChunk Chunk { get; }

    public DensityVolume Volume { get; }

    public TerrainBiomes Biomes { get; }

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
