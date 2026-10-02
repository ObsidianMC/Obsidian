using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang.Carvers;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Fluids;
using System.Runtime.CompilerServices;

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
    private readonly FeatureDecorator featureDecorator;

    // Vanilla's WorldGenRegion random factory (RandomState.getOrCreateRandomFactory("worldgen_region_random")).
    private readonly IPositionalRandomFactory regionRandom;

    // Like vanilla's ProtoChunk.getOrCreateNoiseChunk, one noise chunk serves a chunk from the biomes step to the carvers
    // step, so its flat caches, preliminary surface levels and aquifer carry over between steps.
    private readonly ConditionalWeakTable<IChunk, NoiseChunk> noiseChunks = new();

    public RandomState RandomState { get; }

    // Vanilla's level biome lookup (BiomeManager with the obfuscated seed) over the noise biomes.
    private BiomeManager BiomeManager => field ??= new BiomeManager(this.biomeSource, this.RandomState.Seed, this.dimension.MinY, this.dimension.Height);

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
    public ChunkBuilder(MojangDimension dimension, long seed, bool generateStructures = true)
    {
        this.dimension = dimension;
        this.settings = NoiseRegistry.NoiseSettings.All[dimension.NoiseSettings];
        this.RandomState = new RandomState(this.settings, seed);

        this.terrainGenerator = new TerrainGenerator(this.RandomState);
        this.biomeSource = dimension.CreateBiomeSource(this.RandomState);
        this.surfaceBuilder = new SurfaceBuilder(this.RandomState, this.biomeSource);
        this.carverStep = new CarverStep(this.RandomState, this.surfaceBuilder, this.biomeSource, dimension.Carvers);
        this.featureDecorator = new FeatureDecorator(this.biomeSource.PossibleBiomes, BiomeFeatures.All, this.settings.Noise.Height);
        this.regionRandom = this.RandomState.Random.FromHashOf("minecraft:worldgen_region_random").ForkPositional();

        if (generateStructures)
            this.Structures = new StructureManager(this.RandomState, this.biomeSource, this.terrainGenerator, dimension.MinY, dimension.Height);
    }

    /// <summary>
    /// Fills the chunk from the noise, reshaped around nearby structures. Fluids that need an update are added to the chunk's
    /// post-processing.
    /// </summary>
    public void Generate3DTerrain(IChunk chunk)
    {
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
        this.surfaceBuilder.BuildSurface(chunk, this.GetNoiseChunk(chunk));
        WorldgenHeightmaps.Update(chunk, this.dimension.MinY, this.dimension.Height);
    }

    /// <summary>
    /// Carves caves and canyons. Fluids that need an update are added to the chunk's post-processing.
    /// </summary>
    public void ApplyCarvers(IChunk chunk)
    {
        this.carverStep.Apply(chunk, (chunk as Chunk)?.PostProcessing, this.GetNoiseChunk(chunk));
        this.noiseChunks.Remove(chunk);
        WorldgenHeightmaps.Update(chunk, this.dimension.MinY, this.dimension.Height);
    }

    private NoiseChunk GetNoiseChunk(IChunk chunk) =>
        this.noiseChunks.GetValue(chunk, chunk => new NoiseChunk(this.RandomState, chunk.X, chunk.Z));

    /// <summary>
    /// Places the biome features of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>).
    /// </summary>
    /// <param name="area">The chunk and its 8 neighbors, all past the carvers step. Features may write into any of them.</param>
    /// <param name="onPlaced">Optional callback for each placed feature (step, global index, feature, placed anything).</param>
    public void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int chunkX, int chunkZ,
        Action<int, int, PlacedFeature, bool>? onPlaced = null)
    {
        var region = this.CreateRegion(area, chunkX, chunkZ, trackHeightmaps: true);

        // Like vanilla, only the 3x3 chunks around the decorated chunk contribute biomes.
        var neighbors = area.Where(entry => Math.Abs(entry.Key.X - chunkX) <= 1 && Math.Abs(entry.Key.Z - chunkZ) <= 1)
            .Select(entry => entry.Value)
            .ToArray();

        this.featureDecorator.Decorate(region, neighbors, chunkX, chunkZ, this.Structures, this.terrainGenerator, onPlaced);
    }

    /// <summary>
    /// The block (with Y 0) whose climate best fits the noise settings' spawn target, like vanilla's
    /// <c>Climate.Sampler.findSpawnPosition</c>; the origin when the settings have no target.
    /// </summary>
    /// <summary>
    /// The biome at a block, like vanilla's <c>Level.getBiome</c> for chunks that aren't loaded.
    /// </summary>
    public BiomeCodec GetBiome(int x, int y, int z) => this.BiomeManager.GetBiome(x, y, z);

    public (int X, int Z) FindClimateSpawn()
    {
        var targets = this.settings.SpawnTarget.Select(target => new ParameterPoint(
            Quantize(target.Temperature),
            Quantize(target.Humidity),
            Quantize(target.Continentalness),
            Quantize(target.Erosion),
            new ClimateParameter(Climate.Quantize((float)target.Depth), Climate.Quantize((float)target.Depth)),
            Quantize(target.Weirdness),
            Climate.Quantize((float)target.Offset))).ToArray();

        return SpawnFinder.FindClimateSpawn(targets, new ClimateSampler(this.RandomState.Router));

        static ClimateParameter Quantize(double[] range) => new(Climate.Quantize((float)range[0]), Climate.Quantize((float)range[1]));
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
