using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Generators.Terrain;
using Obsidian.WorldData.Structures;
using System.Text.Json;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Runs the 26.3 biome, terrain and features statuses without switching the live generator.</summary>
internal sealed class BiomeTerrain
{
    private readonly TerrainGenerator terrain;
    private readonly BiomeManager manager;
    private readonly FeatureRegistry? features;
    private readonly int unlitBrightness;
    private readonly TerrainColumns columns;
    private BiomeDecoration? decoration;

    /// <param name="features">
    /// The features to decorate with. Decorating needs one whose template lookup finds 26.3 templates (the server's
    /// extracted ones by default); biomes and terrain don't need it.
    /// </param>
    /// <param name="structures">Whether structures start, beard the terrain and are placed; they need
    /// <paramref name="features"/>, whose templates and features they place.</param>
    public BiomeTerrain(TerrainGenerator terrain, string preset, FeatureRegistry? features = null,
        bool structures = false)
    {
        this.terrain = terrain;
        this.features = features;
        var dimension = preset == "minecraft:nether" ? "minecraft:the_nether" : preset;
        var dimensionType = terrain.Data.Read("dimension_type", dimension);
        var hasSkylight = dimensionType.GetProperty("has_skylight").GetBoolean();
        this.unlitBrightness = hasSkylight ? 15 : 0;
        this.Source = new ClimateSource(terrain.Data, terrain.Router, preset);
        // Point lookups must revisit the tree: memoizing a quart changes later exact ties' previous-leaf input.
        this.manager = new BiomeManager(this.Source, terrain.Seed, terrain.Settings.MinY,
            terrain.Settings.Height, cacheNoiseBiomes: false, clampToHeight: false);
        this.columns = new TerrainColumns(terrain);
        this.MinY = dimensionType.GetProperty("min_y").GetInt32();
        this.Height = dimensionType.GetProperty("height").GetInt32();

        if (structures)
        {
            this.Structures = this.CreateStructures();
        }
    }

    public ClimateSource Source { get; }

    /// <summary>The dimension's build range, which its chunks have.</summary>
    public int MinY { get; }

    public int Height { get; }

    /// <summary>The 26.3 structures' starts and references, or <c>null</c> when structures are off.</summary>
    public StructureManager? Structures { get; }

    private StructureManager CreateStructures()
    {
        var features = this.features
            ?? throw new InvalidOperationException("Structures need a feature registry with a 26.3 template lookup.");
        var possible = this.Source.PossibleBiomes
            .Select(biome => CodecRegistry.TryGetBiome(biome, out var codec) ? codec! : throw Unknown(biome))
            .ToArray();
        var world = new StructureWorld(
            this.terrain.Seed,
            this.Source,
            possible,
            this.terrain.Settings.MinY,
            this.terrain.Settings.Height);
        var data = StructureData.Read(features);

        return new StructureManager(
            world,
            this.columns,
            this.MinY,
            this.Height,
            structures: () => data,
            templates: features.Template);

        static InvalidOperationException Unknown(string biome) => new($"Unknown biome '{biome}'.");
    }

    /// <summary>The decoration status. Throws before writing any block unless a feature registry was given.</summary>
    public BiomeDecoration Decoration => this.decoration ??= this.CreateDecoration();

    private BiomeDecoration CreateDecoration()
    {
        // The default registry's template lookup throws, which would stop decoration halfway through a chunk.
        if (this.features is null)
            throw new InvalidOperationException("Decorating needs a feature registry with a 26.3 template lookup.");

        return new BiomeDecoration(this.features, this.Source.PossibleBiomes);
    }

    /// <summary>
    /// Decorates one terrain-status chunk. Its eight terrain-status neighbors must already exist, and all writes
    /// are retained for later decoration. The live generator does not call this 26.3 pipeline yet.
    /// </summary>
    public WorldGenRegion Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        var center = chunks[(chunkX, chunkZ)];
        var region = this.CreateRegion(chunks, chunkX, chunkZ);
        var neighbors = new List<IChunk>();

        for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
        {
            for (var x = chunkX - 1; x <= chunkX + 1; x++)
            {
                neighbors.Add(chunks[(x, z)]);
            }
        }

        this.Decoration.Generate(
            region,
            center,
            neighbors,
            this.terrain.Settings.MinY,
            this.terrain.Settings.Height,
            this.StructurePlacer(region, chunkX, chunkZ));

        return region;
    }

    /// <summary>
    /// Places only the structures reaching a terrain-status chunk, each step with the seeds decoration gives them,
    /// as <see cref="Decorate"/> does before each step's features. Its eight neighbors must exist.
    /// </summary>
    public WorldGenRegion PlaceStructures(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        var region = this.CreateRegion(chunks, chunkX, chunkZ);
        var place = this.StructurePlacer(region, chunkX, chunkZ);
        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        var seed = random.SetDecorationSeed(region.Seed, chunkX << 4, chunkZ << 4);

        for (var step = 0; step < Enum.GetValues<DecorationStep>().Length; step++)
        {
            place?.Invoke(step, seed, random);
        }

        return region;
    }

    private WorldGenRegion CreateRegion(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        var center = chunks[(chunkX, chunkZ)];
        var random = this.terrain.Compiler.RandomFactory("minecraft:worldgen_region_random")
            .At(chunkX << 4, 0, chunkZ << 4);

        return new WorldGenRegion(chunks, chunkX, chunkZ, this.terrain.Seed, center.MinY, center.Height,
            this.terrain.Settings.SeaLevel, this.Source, random, trackHeightmaps: true)
        {
            UnlitBrightness = this.unlitBrightness
        };
    }

    // Vanilla's applyBiomeDecoration: before each step's features, the step's structures in registry order, each
    // seeded by its index among them, place the starts reaching the chunk inside the chunk (getWritableArea).
    private Action<int, long, WorldgenRandom>? StructurePlacer(WorldGenRegion region, int chunkX, int chunkZ)
    {
        if (this.Structures is not StructureManager structures)
            return null;

        var reaching = structures.FindReaching(chunkX, chunkZ);
        var writableArea = BlockBox.Create(
            chunkX << 4,
            region.MinY + 1,
            chunkZ << 4,
            (chunkX << 4) + 15,
            region.MinY + region.Height - 1,
            (chunkZ << 4) + 15);
        var generation = new WorldGenerationContext(
            Math.Max(region.MinY, this.terrain.Settings.MinY),
            Math.Min(region.Height, this.terrain.Settings.Height));

        return (step, seed, random) =>
        {
            if (step >= reaching.Layout.StructuresPerStep.Count)
                return;

            var stepStructures = reaching.Layout.StructuresPerStep[step];
            using var templates = StructureRegistry.Use(structures.Templates);

            for (var index = 0; index < stepStructures.Count; index++)
            {
                if (!reaching.Reaches(stepStructures[index]))
                    continue;

                random.SetFeatureSeed(seed, index, step);
                var context = new StructurePieceContext(region, random, writableArea, chunkX, chunkZ, default)
                {
                    Terrain = this.columns,
                    Generation = generation
                };

                foreach (var start in reaching.Of(stepStructures[index]))
                {
                    start.PlaceInChunk(context);
                }
            }
        };
    }

    public string BiomeAt(int x, int y, int z) => this.manager.GetBiome(x, y, z).Name;

    public string[] CarversAt(int chunkX, int chunkZ)
    {
        var biome = this.Source.GetNoiseBiome(chunkX << 2, 0, chunkZ << 2);
        var data = this.terrain.Data.Read("biome", biome.Name);
        var carvers = data.GetProperty("carvers");

        if (carvers.ValueKind == JsonValueKind.String)
        {
            return [carvers.GetString()!];
        }

        return carvers.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        this.Source.FillBiomes(chunk);
        fluidUpdates ??= (chunk as Chunk)?.PostProcessing;
        var beardifier = this.Structures is null
            ? null
            : Beardifier.ForStructuresInChunk(
                this.Structures.GetTerrainAdaptingStarts(chunk.X, chunk.Z),
                chunk.X,
                chunk.Z);
        this.terrain.Generate(chunk, this.BiomeAt, this.CarversAt, fluidUpdates, beardifier);

        // Chunk storage keeps the legacy enum until the live pipeline switches; carvers saves as 26.3 terrain.
        chunk.SetChunkStatus(ChunkGenStage.carvers);
    }
}
