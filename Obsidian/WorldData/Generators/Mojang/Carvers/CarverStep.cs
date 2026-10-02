using Obsidian.API.World.Generator.RandomSources;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// The carvers step of vanilla's NoiseBasedChunkGenerator: runs every carver started within 8 chunks.
/// </summary>
internal sealed class CarverStep
{
    private const int Radius = 8;

    private static readonly ConcurrentDictionary<string, IConfiguredCarver> loadedCarvers = new();

    private readonly RandomState randomState;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IBiomeSource biomeSource;
    private readonly IConfiguredCarver[] carvers;

    // A chunk's carving buffers, kept by each thread for its next chunk. Taken while in use.
    [ThreadStatic]
    private static CarvingBuffers? freeBuffers;

    /// <param name="carvers">Configured carver names in the biomes' order, which decides each carver's seed.</param>
    public CarverStep(RandomState randomState, SurfaceBuilder surfaceBuilder, IBiomeSource biomeSource, IEnumerable<string> carvers)
    {
        this.randomState = randomState;
        this.surfaceBuilder = surfaceBuilder;
        this.biomeSource = biomeSource;
        this.carvers = [.. carvers.Select(name => loadedCarvers.GetOrAdd(name, Load))];
    }

    /// <param name="noiseChunk">The chunk's noise chunk when the other steps share it, or <c>null</c> for a new one.</param>
    public void Apply(IChunk chunk, ICollection<Vector>? fluidUpdates = null, NoiseChunk? noiseChunk = null)
    {
        var settings = this.randomState.Settings;
        noiseChunk ??= new NoiseChunk(this.randomState, chunk.X, chunk.Z);

        var buffers = freeBuffers is not null && freeBuffers.MinY == settings.Noise.MinY && freeBuffers.Height == settings.Noise.Height
            ? freeBuffers
            : new CarvingBuffers(settings.Noise.MinY, settings.Noise.Height);
        freeBuffers = null;
        buffers.Mask.Clear();

        var context = new CarvingContext(this.surfaceBuilder, settings.Noise.MinY, settings.Noise.Height, buffers)
        {
            Chunk = chunk,
            NoiseChunk = noiseChunk,
            Aquifer = noiseChunk.Aquifer,
            Biomes = new BiomeManager(this.biomeSource, this.randomState.Seed, settings.Noise.MinY, settings.Noise.Height),
            FluidUpdates = fluidUpdates
        };

        // Carvers read the aquifer's last answer even where the lava level picks the block (see WorldCarver.CarveBlock),
        // so they start from a cleared one rather than the terrain fill's.
        context.Aquifer.ResetFluidUpdate();

        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        var carvers = this.carvers;

        for (var offsetX = -Radius; offsetX <= Radius; offsetX++)
        {
            for (var offsetZ = -Radius; offsetZ <= Radius; offsetZ++)
            {
                var startChunkX = chunk.X + offsetX;
                var startChunkZ = chunk.Z + offsetZ;

                for (var index = 0; index < carvers.Length; index++)
                {
                    random.SetLargeFeatureSeed(this.randomState.Seed + index, startChunkX, startChunkZ);

                    if (carvers[index].IsStartChunk(random))
                        carvers[index].Carve(context, random, startChunkX, startChunkZ);
                }
            }
        }

        freeBuffers = buffers;
    }

    private static IConfiguredCarver Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.configured_carver.{name}.json")
            ?? throw new InvalidOperationException($"Missing configured carver '{name}'.");
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        var config = root.GetProperty("config");
        var type = root.GetProperty("type").GetString();

        var probability = config.GetProperty("probability").GetSingle();
        var y = HeightProvider.Parse(config.GetProperty("y"));
        var yScale = FloatProvider.Parse(config.GetProperty("yScale"));
        var lavaLevel = CarverAnchor.Parse(config.GetProperty("lava_level"));
        var replaceable = ResolveBlockTag(config.GetProperty("replaceable").GetString()!);

        CaveCarverConfiguration CaveConfiguration() => new()
        {
            Probability = probability,
            Y = y,
            YScale = yScale,
            LavaLevel = lavaLevel,
            Replaceable = replaceable,
            HorizontalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("horizontal_radius_multiplier")),
            VerticalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("vertical_radius_multiplier")),
            FloorLevel = FloatProvider.Parse(config.GetProperty("floor_level"))
        };

        return type switch
        {
            "minecraft:cave" => new ConfiguredCarver<CaveCarverConfiguration>(new CaveWorldCarver(), CaveConfiguration()),
            "minecraft:nether_cave" => new ConfiguredCarver<CaveCarverConfiguration>(new NetherWorldCarver(), CaveConfiguration()),
            "minecraft:canyon" => new ConfiguredCarver<CanyonCarverConfiguration>(new CanyonWorldCarver(), new CanyonCarverConfiguration
            {
                Probability = probability,
                Y = y,
                YScale = yScale,
                LavaLevel = lavaLevel,
                Replaceable = replaceable,
                VerticalRotation = FloatProvider.Parse(config.GetProperty("vertical_rotation")),
                DistanceFactor = FloatProvider.Parse(config.GetProperty("shape").GetProperty("distance_factor")),
                Thickness = FloatProvider.Parse(config.GetProperty("shape").GetProperty("thickness")),
                WidthSmoothness = config.GetProperty("shape").GetProperty("width_smoothness").GetInt32(),
                HorizontalRadiusFactor = FloatProvider.Parse(config.GetProperty("shape").GetProperty("horizontal_radius_factor")),
                VerticalRadiusDefaultFactor = config.GetProperty("shape").GetProperty("vertical_radius_default_factor").GetSingle(),
                VerticalRadiusCenterFactor = config.GetProperty("shape").GetProperty("vertical_radius_center_factor").GetSingle()
            }),
            _ => throw new NotSupportedException($"Unsupported carver type '{type}'.")
        };
    }

    private static bool[] ResolveBlockTag(string tag)
    {
        var name = tag.TrimStart('#').Replace("minecraft:", string.Empty);
        var match = TagsRegistry.Block.All.FirstOrDefault(blockTag => blockTag.Name == name)
            ?? throw new InvalidOperationException($"Unknown block tag '{tag}'.");

        var flags = new bool[match.Entries.DefaultIfEmpty(-1).Max() + 1];
        foreach (var id in match.Entries)
            flags[id] = true;

        return flags;
    }

    private interface IConfiguredCarver
    {
        public bool IsStartChunk(IRandomSource random);

        public void Carve(CarvingContext context, IRandomSource random, int startChunkX, int startChunkZ);
    }

    private sealed class ConfiguredCarver<TConfiguration> : IConfiguredCarver where TConfiguration : CarverConfiguration
    {
        private readonly WorldCarver<TConfiguration> carver;
        private readonly TConfiguration configuration;

        public ConfiguredCarver(WorldCarver<TConfiguration> carver, TConfiguration configuration)
        {
            this.carver = carver;
            this.configuration = configuration;
        }

        public bool IsStartChunk(IRandomSource random) => this.carver.IsStartChunk(this.configuration, random);

        public void Carve(CarvingContext context, IRandomSource random, int startChunkX, int startChunkZ) =>
            this.carver.Carve(context, this.configuration, random, startChunkX, startChunkZ);
    }
}
