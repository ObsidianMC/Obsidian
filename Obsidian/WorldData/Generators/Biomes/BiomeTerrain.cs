using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Plugins;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Generators.Mojang.Regions;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Generators.Terrain;
using Obsidian.WorldData.Structures;
using System.Collections.Concurrent;
using System.Text.Json;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Runs the 26.3 biome, terrain and features statuses without switching the live generator.</summary>
/// <remarks>
/// A dimension made of plugins' types (see <see cref="WorldgenDimension.Plugins"/>) enters them for each step that
/// can run their code: a plugin unloading stops it with a <see cref="GeneratorStoppedException"/>, and their code
/// failing throws a <see cref="PluginGenerationException"/> naming them. Either only concerns this dimension's
/// generator; other dimensions, and other worlds of this one, keep generating.
/// </remarks>
internal sealed class BiomeTerrain
{
    private readonly TerrainGenerator terrain;
    private readonly BiomeManager manager;
    private readonly FeatureRegistry? features;
    private readonly int unlitBrightness;
    private readonly TerrainColumns columns;
    private readonly long zoomSeed;

    // Each biome's carvers, read once: every chunk asks for the 289 chunks its carvers can start in.
    private readonly ConcurrentDictionary<string, string[]> carversByBiome = new();
    private BiomeDecoration? decoration;

    /// <param name="features">
    /// The features to decorate with. Decorating needs one whose template lookup finds 26.3 templates (the server's
    /// extracted ones by default); biomes and terrain don't need it.
    /// </param>
    /// <param name="structures">Whether structures start, beard the terrain and are placed; they need
    /// <paramref name="features"/>, whose templates and features they place.</param>
    /// <param name="preset">The biome preset of one of vanilla's dimensions: minecraft:overworld, minecraft:nether
    /// or minecraft:the_end.</param>
    public BiomeTerrain(TerrainGenerator terrain, string preset, FeatureRegistry? features = null,
        bool structures = false)
        : this(
            terrain,
            VanillaDimension(preset),
            VanillaDimension(preset),
            new ClimateSource(terrain.Data, terrain.Router, preset),
            [],
            features,
            structures)
    {
    }

    private BiomeTerrain(
        TerrainGenerator terrain,
        string identifier,
        string dimension,
        ClimateSource source,
        ImmutableArray<PluginContainer> plugins,
        FeatureRegistry? features,
        bool structures)
    {
        this.terrain = terrain;
        this.features = features;
        this.Identifier = identifier;
        this.Plugins = plugins;
        var dimensionType = terrain.Data.Read("dimension_type", dimension);
        var hasSkylight = dimensionType.GetProperty("has_skylight").GetBoolean();
        this.unlitBrightness = hasSkylight ? 15 : 0;
        this.Source = source;
        // Point lookups must revisit the tree: memoizing a quart changes later exact ties' previous-leaf input.
        this.manager = new BiomeManager(this.Source, terrain.Seed, terrain.Settings.MinY,
            terrain.Settings.Height, cacheNoiseBiomes: false, clampToHeight: false);
        this.columns = new TerrainColumns(terrain);
        this.zoomSeed = BiomeManager.ObfuscateSeed(terrain.Seed);
        this.MinY = dimensionType.GetProperty("min_y").GetInt32();
        this.Height = dimensionType.GetProperty("height").GetInt32();

        if (structures)
        {
            this.Structures = this.CreateStructures();
        }
    }

    /// <summary>
    /// Builds the 26.3 statuses of a dimension, vanilla's or a plugin's, for a world. Its biome regions are fixed
    /// from then on (see <see cref="WorldgenDimension.FixRegions"/>).
    /// </summary>
    /// <param name="features">As for the constructor.</param>
    /// <param name="structures">As for the constructor.</param>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    /// <exception cref="GeneratorStoppedException">A plugin it's made of is unloading.</exception>
    /// <exception cref="PluginGenerationException">A plugin's code failed building it.</exception>
    public static BiomeTerrain Create(
        WorldgenDimension dimension,
        long seed,
        FeatureRegistry? features = null,
        bool structures = false)
    {
        // Taken before entering the plugins, so that a released dimension says so, whether or not they unloaded.
        var settings = dimension.Settings;
        var biomeLayout = dimension.Layout;
        var densityTypes = dimension.DensityTypes;

        using var calls = GeneratorCalls.Enter(dimension.Identifier, dimension.Plugins);

        try
        {
            var terrain = new TerrainGenerator(dimension.Data, seed, settings, types: densityTypes);
            var regions = dimension.FixRegions();
            var regionLayout = regions.IsDefaultOrEmpty
                ? null
                : new RegionLayout(dimension.Identifier, regions, terrain.Compiler.Random);
            var source = new ClimateSource(terrain.Router, biomeLayout, regionLayout);

            return new BiomeTerrain(
                terrain,
                dimension.Identifier,
                dimension.DimensionType,
                source,
                dimension.Plugins,
                features,
                structures);
        }
        catch (Exception ex) when (Contains(dimension.Plugins, ex))
        {
            throw Failed(dimension.Identifier, dimension.Plugins, ex);
        }
    }

    /// <summary>The dimension's identifier, its level stem's.</summary>
    public string Identifier { get; }

    /// <summary>The plugins whose types the dimension is made of, which each step enters.</summary>
    public ImmutableArray<PluginContainer> Plugins { get; }

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
        using var calls = GeneratorCalls.Enter(this.Identifier, this.Plugins);

        try
        {
            return this.DecorateEntered(chunks, chunkX, chunkZ);
        }
        catch (Exception ex) when (Contains(this.Plugins, ex))
        {
            throw Failed(this.Identifier, this.Plugins, ex);
        }
    }

    private WorldGenRegion DecorateEntered(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
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
    /// Stores decoration's final heights once every neighboring decoration that can write this chunk has finished.
    /// Call before lighting and publishing the chunk; the worldgen maps retain their terrain-stage values.
    /// </summary>
    public void CompleteHeightmaps(IChunk chunk)
    {
        WorldgenHeightmaps.UpdateFinal(chunk, chunk.MinY, chunk.Height);
        if (chunk is Chunk generated)
            generated.FinalHeightmaps = null;
    }

    /// <summary>
    /// Places only the structures reaching a terrain-status chunk, each step with the seeds decoration gives them,
    /// as <see cref="Decorate"/> does before each step's features. Its eight neighbors must exist.
    /// </summary>
    public WorldGenRegion PlaceStructures(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        using var calls = GeneratorCalls.Enter(this.Identifier, this.Plugins);

        try
        {
            return this.PlaceStructuresEntered(chunks, chunkX, chunkZ);
        }
        catch (Exception ex) when (Contains(this.Plugins, ex))
        {
            throw Failed(this.Identifier, this.Plugins, ex);
        }
    }

    private WorldGenRegion PlaceStructuresEntered(
        IReadOnlyDictionary<(int X, int Z), IChunk> chunks,
        int chunkX,
        int chunkZ)
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

    /// <summary>A block's biome over uncached climate searches, as carving's top materials read it.</summary>
    public string BiomeAt(int x, int y, int z) => this.manager.GetBiome(x, y, z).Name;

    /// <summary>
    /// The biomes one chunk's terrain status reads. Its surface reads the biomes stored in the chunk, which must be
    /// filled (<see cref="ClimateSource.FillBiomes"/>), and in its neighbors, as vanilla's terrain reads them through
    /// its region's biome manager. Carving reads <see cref="BiomeAt"/>. Use them on one thread.
    /// </summary>
    /// <param name="neighbors">
    /// The loaded chunk at a chunk position, or <c>null</c>. A neighbor loaded at the biomes status or later is read
    /// from its own storage, whatever filled it; the biomes of any other neighbor are filled as its biomes status would
    /// fill them (<see cref="ClimateSource.Stored"/>).
    /// </param>
    public TerrainBiomes ChunkBiomes(IChunk chunk, Func<int, int, IChunk?>? neighbors = null)
    {
        var source = new SurfaceBiomeSource(this.Source, chunk, neighbors);
        var table = new SurfaceBiomes(source, this.zoomSeed, chunk.X, chunk.Z, chunk.MinY, chunk.Height);
        BiomeManager? outside = null;

        // The table answers the chunk's own blocks; any other block goes through a manager over the same quarts.
        string SurfaceAt(int x, int y, int z)
        {
            if (table.Get(x, y, z) is BiomeCodec biome)
                return biome.Name;

            if (outside is null)
            {
                outside = new BiomeManager(
                    source,
                    this.terrain.Seed,
                    chunk.MinY,
                    chunk.Height,
                    cacheNoiseBiomes: false,
                    readsNeighbors: true);
                outside.FocusOn(chunk.X, chunk.Z);
            }

            return outside.GetBiome(x, y, z).Name;
        }

        return new TerrainBiomes(Surface: SurfaceAt, Carving: this.BiomeAt) { SurfaceNames = table.Names };
    }

    /// <summary>The carvers of the biome a chunk's carvers start in. Callers mustn't change the list.</summary>
    public string[] CarversAt(int chunkX, int chunkZ)
    {
        var biome = this.Source.GetNoiseBiome(chunkX << 2, 0, chunkZ << 2);
        return this.carversByBiome.GetOrAdd(biome.Name, this.ReadCarvers);
    }

    private string[] ReadCarvers(string biome)
    {
        var data = this.terrain.Data.Read("biome", biome);
        var carvers = data.GetProperty("carvers");

        if (carvers.ValueKind == JsonValueKind.String)
        {
            return [carvers.GetString()!];
        }

        return carvers.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    /// <param name="neighbors">
    /// The loaded chunks whose stored biomes the surface reads (see <see cref="ChunkBiomes"/>).
    /// </param>
    public void Generate(
        IChunk chunk,
        ICollection<Vector>? fluidUpdates = null,
        Func<int, int, IChunk?>? neighbors = null)
    {
        using var calls = GeneratorCalls.Enter(this.Identifier, this.Plugins);

        try
        {
            this.GenerateEntered(chunk, fluidUpdates, neighbors);
        }
        catch (Exception ex) when (Contains(this.Plugins, ex))
        {
            throw Failed(this.Identifier, this.Plugins, ex);
        }
    }

    private void GenerateEntered(IChunk chunk, ICollection<Vector>? fluidUpdates, Func<int, int, IChunk?>? neighbors)
    {
        using var writes = (chunk as Chunk)?.BeginTerrainWrites();
        this.Source.FillBiomes(chunk);
        fluidUpdates ??= (chunk as Chunk)?.PostProcessing;
        this.terrain.Generate(
            chunk,
            this.ChunkBiomes(chunk, neighbors),
            this.CarversAt,
            fluidUpdates,
            this.BeardifierFor(chunk.X, chunk.Z));

        // Chunk storage keeps the legacy enum until the live pipeline switches; carvers saves as 26.3 terrain.
        chunk.SetChunkStatus(ChunkGenStage.carvers);
    }

    // A dimension made of plugins' types blames them for what fails while it generates, except their unloading.
    private static bool Contains(ImmutableArray<PluginContainer> plugins, Exception exception) =>
        !plugins.IsDefaultOrEmpty
        && exception is not (GeneratorStoppedException or PluginGenerationException or OutOfMemoryException);

    private static PluginGenerationException Failed(
        string identifier,
        ImmutableArray<PluginContainer> plugins,
        Exception exception)
    {
        var names = string.Join(", ", plugins.Select(plugin => plugin.Info.Name));

        return new PluginGenerationException(
            plugins,
            $"{identifier} failed generating with {names}: {exception.Message}",
            exception);
    }

    private static string VanillaDimension(string preset) =>
        preset == "minecraft:nether" ? "minecraft:the_nether" : preset;

    /// <summary>The density of the structures around a chunk, or <c>null</c> when structures are off.</summary>
    public Beardifier? BeardifierFor(int chunkX, int chunkZ)
    {
        if (this.Structures is null)
            return null;

        // Contributions add up in registry order of the structures; vanilla walks a hash map, so float sums of
        // overlapping structures of different types could round differently (no capture shows it).
        var starts = this.Structures.GetTerrainAdaptingStarts(chunkX, chunkZ);

        return Beardifier.ForStructuresInChunk(starts, chunkX, chunkZ);
    }

    /// <summary>
    /// The biomes stored in a chunk and its neighbors, by quart, as a region reads them. A block's zoomed lookup
    /// reaches one quart past the chunk, so only the eight neighbors are asked for.
    /// </summary>
    private sealed class SurfaceBiomeSource(ClimateSource source, IChunk chunk, Func<int, int, IChunk?>? neighbors)
        : IBiomeSource
    {
        // The neighbor read last, as its loaded chunk or else its filled biomes: a column's blocks read one neighbor's
        // quarts after another.
        private bool hasNeighbor;
        private int neighborX;
        private int neighborZ;
        private IChunk? loadedNeighbor;
        private StoredBiomes? filledNeighbor;

        /// <param name="quartY">A quart y within the chunk's build range.</param>
        public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
        {
            var chunkX = quartX >> 2;
            var chunkZ = quartZ >> 2;

            if (chunkX == chunk.X && chunkZ == chunk.Z)
                return chunk.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2);

            if (!this.hasNeighbor || this.neighborX != chunkX || this.neighborZ != chunkZ)
                this.MoveTo(chunkX, chunkZ);

            if (this.loadedNeighbor is IChunk loaded)
                return loaded.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2);

            return this.filledNeighbor!.Get(quartX & 3, quartY - (chunk.MinY >> 2), quartZ & 3);
        }

        private void MoveTo(int chunkX, int chunkZ)
        {
            var loaded = neighbors?.Invoke(chunkX, chunkZ);
            var hasBiomes = loaded is not null && loaded.ChunkStatus >= ChunkGenStage.biomes;

            this.loadedNeighbor = hasBiomes ? loaded : null;
            this.filledNeighbor = hasBiomes ? null : source.Stored(chunkX, chunkZ, chunk.MinY, chunk.Height);
            this.neighborX = chunkX;
            this.neighborZ = chunkZ;
            this.hasNeighbor = true;
        }
    }
}
