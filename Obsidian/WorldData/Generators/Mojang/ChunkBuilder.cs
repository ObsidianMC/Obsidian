using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang.Carvers;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Fluids;
using Obsidian.Plugins;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Runs the vanilla generation steps (biomes, noise, surface, carvers, features) of a dimension for a level.
/// </summary>
internal sealed class ChunkBuilder
{
    private readonly MojangDimension dimension;
    private readonly NoiseSetting settings;
    private readonly TerrainGenerator terrainGenerator;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IClimateBiomeSource biomeSource;
    private readonly CarverStep carverStep;

    // The decorator of the current biome features, rebuilt when plugins change them (see CurrentDecoration).
    private readonly Func<WorldgenFeatureSet> features;
    private readonly Lock decorationLock = new();
    private Decoration decoration;
    private WeakReference<WorldgenFeatureSet>? rejected;

    // Vanilla's WorldGenRegion random factory (RandomState.getOrCreateRandomFactory("worldgen_region_random")).
    private readonly IPositionalRandomFactory regionRandom;

    // Like vanilla's ProtoChunk.getOrCreateNoiseChunk, one noise chunk serves a chunk from the biomes step to the carvers
    // step, so its flat caches, preliminary surface levels and aquifer carry over between steps.
    private readonly ConditionalWeakTable<IChunk, NoiseChunk> noiseChunks = new();
    private readonly ConditionalWeakTable<IChunk, NoiseChunk>.CreateValueCallback rentNoiseChunk;

    // Noise chunks of chunks past the carvers step, which move to new chunks rather than new ones being built: a new one
    // maps and compiles the router and allocates its caches. At most one per processor is kept (a few hundred KB each).
    private readonly ConcurrentBag<NoiseChunk> freeNoiseChunks = [];
    private readonly int maxFreeNoiseChunks = Environment.ProcessorCount;
    private int freeNoiseChunkCount;

    public RandomState RandomState { get; }

    // Vanilla's level biome lookup (BiomeManager with the obfuscated seed) over the noise biomes. It caches, so it's used
    // under its lock: explorer maps can be made on several threads at once.
    private readonly Lock biomeLock = new();
    private BiomeManager BiomeManager =>
        field ??= new BiomeManager(this.biomeSource, this.RandomState.Seed, this.dimension.MinY, this.dimension.Height);

    /// <summary>
    /// The world's structures, or <c>null</c> when structures aren't generated.
    /// </summary>
    public StructureManager? Structures { get; }

    /// <summary>
    /// Creates a builder for the overworld.
    /// </summary>
    /// <param name="seed">World seed; see <see cref="RandomState.ParseSeed"/> for level seed strings.</param>
    /// <param name="generateStructures">Whether structures generate, like vanilla's <c>generate-structures</c> option.</param>
    public ChunkBuilder(long seed, bool generateStructures = true) : this(MojangDimension.Overworld, seed, generateStructures)
    {
    }

    /// <param name="dimension">The dimension to generate. Chunks passed in must use its build range.</param>
    /// <param name="seed">World seed; see <see cref="RandomState.ParseSeed"/> for level seed strings.</param>
    /// <param name="generateStructures">Whether structures generate, like vanilla's <c>generate-structures</c> option.</param>
    /// <param name="features">The biomes' features, read before each chunk is decorated;
    /// <see cref="PluginFeatures.Current"/> by default.</param>
    /// <param name="startCache">The structure starts of the builder this one replaces, generated with the same seed
    /// and dimension, so the state of their placed pieces carries over; new ones by default.</param>
    /// <param name="structures">The structures and structure sets, read as structures start;
    /// <see cref="PluginStructures.Current"/> by default.</param>
    public ChunkBuilder(MojangDimension dimension, long seed, bool generateStructures = true,
        Func<WorldgenFeatureSet>? features = null, StructureStartCache? startCache = null,
        Func<WorldgenStructures>? structures = null)
    {
        this.dimension = dimension;
        this.settings = dimension.Settings;

        // Binding the noise router runs plugins' density functions' code.
        using var calls = this.EnterGenerator();

        this.RandomState = new RandomState(this.settings, seed, dimension.Noises);

        this.terrainGenerator = new TerrainGenerator(this.RandomState);
        this.biomeSource = dimension.CreateBiomeSource(this.RandomState);
        this.surfaceBuilder = new SurfaceBuilder(this.RandomState, this.biomeSource);
        this.carverStep = new CarverStep(this.RandomState, this.surfaceBuilder, this.biomeSource);
        this.features = features ?? (static () => PluginFeatures.Current);
        this.decoration = this.CreateDecoration(this.features());
        this.regionRandom = this.RandomState.Random.FromHashOf("minecraft:worldgen_region_random").ForkPositional();
        this.rentNoiseChunk = this.RentNoiseChunk;

        if (generateStructures)
        {
            this.Structures = new StructureManager(
                this.RandomState,
                this.biomeSource,
                this.terrainGenerator,
                dimension.MinY,
                dimension.Height,
                startCache,
                structures);
        }
    }

    /// <summary>
    /// Enters the plugins whose types the dimension's noise settings and biome source are made of, whose code every
    /// step runs. Each public step of the builder does; other code using its parts, like structure searches, does it
    /// around them. Steps are a chunk each, so plugins are entered once per chunk rather than per sample, and without
    /// plugin types there's nothing to enter.
    /// </summary>
    /// <exception cref="GeneratorStoppedException">A plugin is unloading: don't generate.</exception>
    public GeneratorCalls EnterGenerator() => GeneratorCalls.Enter(this.dimension);

    /// <summary>
    /// Fills the chunk from the noise, reshaped around nearby structures. Fluids that need an update are added to the chunk's
    /// post-processing.
    /// </summary>
    public void Generate3DTerrain(IChunk chunk)
    {
        using var calls = this.EnterGenerator();

        var beardifier = this.Structures is null
            ? Beardifier.Empty
            : Beardifier.ForStructuresInChunk(this.Structures.GetTerrainAdaptingStarts(chunk.X, chunk.Z), chunk.X, chunk.Z);

        this.terrainGenerator.Generate(chunk, (chunk as Chunk)?.PostProcessing, beardifier, this.GetNoiseChunk(chunk));
    }

    /// <summary>
    /// Stores the biome of every 4x4x4 cell of the chunk, over the whole build range like vanilla.
    /// </summary>
    public void PopulateBiomes(IChunk chunk)
    {
        using var calls = this.EnterGenerator();

        var sampler = this.GetNoiseChunk(chunk).ClimateSampler;
        var minQuartY = this.dimension.MinY >> 2;
        var maxQuartY = minQuartY + (this.dimension.Height >> 2);

        for (var quartX = 0; quartX < 4; quartX++)
        {
            for (var quartZ = 0; quartZ < 4; quartZ++)
            {
                for (var quartY = minQuartY; quartY < maxQuartY; quartY++)
                {
                    var biome = this.biomeSource.GetNoiseBiome(sampler, (chunk.X << 2) + quartX, quartY, (chunk.Z << 2) + quartZ);
                    chunk.SetBiome(quartX << 2, quartY << 2, quartZ << 2, biome);
                }
            }
        }
    }

    public void ApplySurfaceRules(IChunk chunk)
    {
        using var calls = this.EnterGenerator();

        this.surfaceBuilder.BuildSurface(chunk, this.GetNoiseChunk(chunk));
        WorldgenHeightmaps.Update(chunk, this.dimension.MinY, ChunkColumns.NonAirHeight(chunk, this.dimension.MinY, this.dimension.Height));
    }

    /// <summary>
    /// Carves caves and canyons with the current biome carvers (see <see cref="WorldgenFeatureSet.BiomeCarvers"/>).
    /// Fluids that need an update are added to the chunk's post-processing.
    /// </summary>
    public void ApplyCarvers(IChunk chunk)
    {
        using var calls = this.EnterGenerator();

        var noiseChunk = this.GetNoiseChunk(chunk);
        this.carverStep.Apply(chunk, this.features().BiomeCarvers, (chunk as Chunk)?.PostProcessing, noiseChunk);
        this.noiseChunks.Remove(chunk);
        this.ReturnNoiseChunk(noiseChunk);
        WorldgenHeightmaps.Update(chunk, this.dimension.MinY, ChunkColumns.NonAirHeight(chunk, this.dimension.MinY, this.dimension.Height));
    }

    private NoiseChunk GetNoiseChunk(IChunk chunk) => this.noiseChunks.GetValue(chunk, this.rentNoiseChunk);

    private NoiseChunk RentNoiseChunk(IChunk chunk)
    {
        if (!this.freeNoiseChunks.TryTake(out var noiseChunk))
            return new NoiseChunk(this.RandomState, chunk.X, chunk.Z);

        Interlocked.Decrement(ref this.freeNoiseChunkCount);
        noiseChunk.MoveTo(chunk.X, chunk.Z);
        return noiseChunk;
    }

    private void ReturnNoiseChunk(NoiseChunk noiseChunk)
    {
        if (Interlocked.Increment(ref this.freeNoiseChunkCount) <= this.maxFreeNoiseChunks)
            this.freeNoiseChunks.Add(noiseChunk);
        else
            Interlocked.Decrement(ref this.freeNoiseChunkCount);
    }

    /// <summary>
    /// Places the biome features of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>).
    /// </summary>
    /// <param name="area">The chunk and its 8 neighbors, all past the carvers step. Features may write into any of them.</param>
    /// <param name="onPlaced">Optional callback for each placed feature (step, global index, feature, placed anything).</param>
    /// <param name="structures">The structure starts to place, found before (see
    /// <see cref="StructureManager.FindReaching"/>), as when their state was restored meanwhile; found now by
    /// default.</param>
    public void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int chunkX, int chunkZ,
        Action<int, int, PlacedFeature, bool>? onPlaced = null, ReachingStarts? structures = null)
    {
        // Features look biomes and structures up, which runs the generator's plugins' code too.
        using var generatorCalls = this.EnterGenerator();

        structures ??= this.Structures?.FindReaching(chunkX, chunkZ);

        var region = this.CreateRegion(area, chunkX, chunkZ, trackHeightmaps: true);

        // Like vanilla, only the 3x3 chunks around the decorated chunk contribute biomes.
        var neighbors = area.Where(entry => Math.Abs(entry.Key.X - chunkX) <= 1 && Math.Abs(entry.Key.Z - chunkZ) <= 1)
            .Select(entry => entry.Value)
            .ToArray();

        var decoration = this.CurrentDecoration();
        var calls = EnterPlugins(decoration, out var skip);

        try
        {
            decoration.Decorator.Decorate(
                region,
                neighbors,
                chunkX,
                chunkZ,
                structures,
                this.terrainGenerator,
                onPlaced,
                skip);
        }
        catch (FeaturePlacementException ex) when (decoration.Owners.TryGetValue(ex.Feature, out var owners))
        {
            // A feature made of plugins' types ran their code: the failure is theirs.
            throw new PluginGenerationException(owners, ex.Message, ex);
        }
        catch (FeaturePlacementException ex) when (ex.InnerException is PluginGenerationException inner)
        {
            // A feature placed one by identifier made of plugins' types (see WorldgenFeatureSet.PlaceConfigured).
            throw new PluginGenerationException(inner.Plugins, ex.Message, ex);
        }
        finally
        {
            for (var i = calls.Length - 1; i >= 0; i--)
                calls[i].Dispose();
        }
    }

    /// <summary>
    /// Moves decoration to the current biome features, if they changed, so the previous ones aren't kept.
    /// </summary>
    /// <returns>
    /// Whether decoration uses the current features: false when they can't be ordered for this dimension, and the
    /// previous ones are kept.
    /// </returns>
    public bool RefreshFeatures() =>
        ReferenceEquals(this.CurrentDecoration().BiomeFeatures, this.features().BiomeFeatures);

    /// <summary>
    /// Moves structures to the current ones, if plugins changed them, dropping the starts of structure sets that are
    /// gone (see <see cref="StructureManager.Refresh"/>).
    /// </summary>
    public void RefreshStructures() => this.Structures?.Refresh();

    // Plugins' feature types run plugin code, so decorating is a call into every plugin whose features it may place,
    // which keeps the plugin from being released (its services disposed, its load context unloaded) until it ends.
    // A plugin that's unloading refuses, and its features are left out, as they are once generators move on.
    // Without plugin types, as with vanilla's features, there's nothing to enter.
    private static PluginCall[] EnterPlugins(Decoration decoration, out Func<PlacedFeature, bool>? skip)
    {
        skip = null;

        if (decoration.Plugins.IsEmpty)
            return [];

        var calls = new List<PluginCall>(decoration.Plugins.Length);
        HashSet<PluginContainer>? refused = null;

        foreach (var plugin in decoration.Plugins)
        {
            if (plugin.TryEnter() is PluginCall call)
                calls.Add(call);
            else
                (refused ??= []).Add(plugin);
        }

        if (refused is not null)
            skip = feature => decoration.Owners.TryGetValue(feature, out var owners) && owners.Any(refused.Contains);

        return [.. calls];
    }

    // The decoration of the current biome features. A chunk is decorated with one decorator throughout; ones being
    // decorated when plugins change finish with the previous features. Like vanilla with the same data pack, the
    // new decorator sorts the features again, which can change the seeds of vanilla's features in a step plugins
    // added to.
    // Features can't be ordered for this dimension when its biomes' features are in a cycle. PluginFeatures doesn't
    // publish such features for a generating dimension, but if any come, decoration keeps the previous ones rather
    // than failing every chunk and, with it, chunk management. Weak, so the plugins they came from aren't kept.
    private Decoration CurrentDecoration()
    {
        var features = this.features();
        var decoration = Volatile.Read(ref this.decoration);
        if (ReferenceEquals(decoration.BiomeFeatures, features.BiomeFeatures) || this.IsRejected(features))
            return decoration;

        lock (this.decorationLock)
        {
            // Read again under the lock, so a slower thread can't put back features older than another's.
            features = this.features();
            if (ReferenceEquals(this.decoration.BiomeFeatures, features.BiomeFeatures) || this.IsRejected(features))
                return this.decoration;

            try
            {
                Volatile.Write(ref this.decoration, this.CreateDecoration(features));
            }
            catch (InvalidOperationException)
            {
                Volatile.Write(ref this.rejected, new WeakReference<WorldgenFeatureSet>(features));
            }

            return this.decoration;
        }
    }

    private bool IsRejected(WorldgenFeatureSet features) =>
        Volatile.Read(ref this.rejected) is { } rejected
        && rejected.TryGetTarget(out var target)
        && ReferenceEquals(target, features);

    // Keeps only what decorating needs of the feature set, so that a plugin's features not in any biome aren't
    // kept after the plugin unloads.
    private Decoration CreateDecoration(WorldgenFeatureSet features)
    {
        var decorator = new FeatureDecorator(
            this.biomeSource.PossibleBiomes,
            features.BiomeFeatures,
            this.settings.Noise.Height);

        return new Decoration(features.BiomeFeatures, features.Owners, features.Plugins, decorator);
    }

    private sealed record Decoration(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>> BiomeFeatures,
        FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>> Owners,
        ImmutableArray<PluginContainer> Plugins,
        FeatureDecorator Decorator);

    /// <summary>
    /// The block (with Y 0) whose climate best fits the noise settings' spawn target, like vanilla's
    /// <c>Climate.Sampler.findSpawnPosition</c>; the origin when the settings have no target.
    /// </summary>
    /// <summary>
    /// The biome at a block, like vanilla's <c>Level.getBiome</c> for chunks that aren't loaded.
    /// </summary>
    public BiomeCodec GetBiome(int x, int y, int z)
    {
        using var calls = this.EnterGenerator();

        lock (this.biomeLock)
            return this.BiomeManager.GetBiome(x, y, z);
    }

    public (int X, int Z) FindClimateSpawn()
    {
        using var calls = this.EnterGenerator();

        var targets = this.settings.SpawnTarget.Select(target => new ParameterPoint(
            Quantize(target.Temperature),
            Quantize(target.Humidity),
            Quantize(target.Continentalness),
            Quantize(target.Erosion),
            new ClimateParameter(Climate.Quantize((float)target.Depth), Climate.Quantize((float)target.Depth)),
            Quantize(target.Weirdness),
            Climate.Quantize((float)target.Offset))).ToArray();

        return SpawnFinder.FindClimateSpawn(targets, this.RandomState.ClimateSampler);

        static ClimateParameter Quantize(ImmutableArray<double> range) => new(Climate.Quantize((float)range[0]), Climate.Quantize((float)range[1]));
    }

    /// <summary>
    /// Vanilla's post-processing of a chunk every feature has reached (<c>LevelChunk.postProcessGeneration</c>): each
    /// marked fluid ticks right away (spreading into the chunk's neighbors as needed), and marked blocks other than liquids
    /// are updated against their neighbors. Ticks scheduled meanwhile get their real delays.
    /// </summary>
    /// <param name="area">The chunk and its 8 neighbors.</param>
    /// <param name="fluidRules">The level's fluid rules; vanilla's defaults for the dimension when <c>null</c>.</param>
    public void PostProcess(IReadOnlyDictionary<(int X, int Z), IChunk> area, int chunkX, int chunkZ, FluidRules? fluidRules = null)
    {
        if (area[(chunkX, chunkZ)] is not Chunk chunk || chunk.PostProcessing.Count == 0)
            return;

        using var calls = this.EnterGenerator();

        var region = this.CreateRegion(area, chunkX, chunkZ, trackHeightmaps: false);
        var minY = this.dimension.MinY;

        // Vanilla post-processes on the live level, whose random only varies lava's spread delay; any seed will do.
        var rules = fluidRules ?? FluidRules.ForDimension(this.dimension.DimensionType.Name, this.dimension.DimensionType.Element.Ultrawarm);
        var fluids = new FluidLevel(new WorldGenFluidAccess(region, rules, new Random(chunkX * 31 + chunkZ)));

        // Vanilla walks the marks section by section, in the order they were added.
        var marks = chunk.PostProcessing.OrderBy(position => (position.Y - minY) >> 4).ToList();
        chunk.PostProcessing.Clear();

        foreach (var position in marks)
        {
            // The fluid's tick may change blocks, but the shape update still starts from the block read before it.
            var block = region.GetBlock(position);
            if (block.HasFluid())
                fluids.TickFluid(position);

            if (block.IsLiquidBlock())
                continue;

            var updated = ShapeUpdater.UpdateFromNeighbourShapes(fluids.ShapeView, block, position);
            if (!updated.IsSameState(block))
                region.SetBlock(position, updated);
        }

        // Fluids may have spread into finished neighbors, whose heightmaps are already stored.
        region.StoreFinishedHeightmaps();
    }

    /// <param name="trackHeightmaps">
    /// Whether the chunks written keep their final heightmaps (see <see cref="Chunk.FinalHeightmaps"/>). Decorations only
    /// write into chunks that are still generating, but post-processing may spread fluids into complete ones.
    /// </param>
    private WorldGenRegion CreateRegion(IReadOnlyDictionary<(int X, int Z), IChunk> area, int chunkX, int chunkZ, bool trackHeightmaps) =>
        new(area, chunkX, chunkZ, this.RandomState.Seed, this.dimension.MinY, this.dimension.Height, this.settings.SeaLevel, this.biomeSource,
            this.regionRandom.At(chunkX << 4, 0, chunkZ << 4), trackHeightmaps);

    /// <summary>
    /// Writes the final heightmaps into the chunk; call it once every chunk around it is decorated and it's post-processed.
    /// </summary>
    public void UpdateFinalHeightmaps(IChunk chunk)
    {
        WorldgenHeightmaps.UpdateFinal(chunk, this.dimension.MinY, this.dimension.Height);

        // From now on the level may change the chunk's blocks, which the tracked heights wouldn't follow.
        if (chunk is Chunk generated)
            generated.FinalHeightmaps = null;
    }
}
