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

    // Every vanilla overworld biome uses these carvers in this order (the order decides each carver's seed).
    private static readonly Lazy<IReadOnlyList<IConfiguredCarver>> overworldCarvers =
        new(() => [Load("cave"), Load("cave_extra_underground"), Load("canyon")]);

    private readonly RandomState randomState;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IBiomeSource biomeSource;
    private readonly FluidPicker fluidPicker;

    public CarverStep(RandomState randomState, SurfaceBuilder surfaceBuilder, IBiomeSource biomeSource)
    {
        this.randomState = randomState;
        this.surfaceBuilder = surfaceBuilder;
        this.biomeSource = biomeSource;

        var settings = randomState.Settings;
        this.fluidPicker = Aquifers.CreateGlobalFluidPicker(settings.SeaLevel, BlocksRegistry.GetFromSimpleState(settings.DefaultFluid));
    }

    public void Apply(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        var settings = this.randomState.Settings;
        var noiseChunk = new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var context = new CarvingContext(this.surfaceBuilder, settings.Noise.MinY, settings.Noise.Height)
        {
            Chunk = chunk,
            NoiseChunk = noiseChunk,
            Aquifer = Aquifers.Create(noiseChunk, chunk.X, chunk.Z, this.fluidPicker),
            Biomes = new BiomeManager(this.biomeSource, this.randomState.Seed, settings.Noise.MinY, settings.Noise.Height),
            FluidUpdates = fluidUpdates
        };

        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        var carvers = overworldCarvers.Value;

        for (var offsetX = -Radius; offsetX <= Radius; offsetX++)
        {
            for (var offsetZ = -Radius; offsetZ <= Radius; offsetZ++)
            {
                var startChunkX = chunk.X + offsetX;
                var startChunkZ = chunk.Z + offsetZ;

                for (var index = 0; index < carvers.Count; index++)
                {
                    random.SetLargeFeatureSeed(this.randomState.Seed + index, startChunkX, startChunkZ);

                    if (carvers[index].IsStartChunk(random))
                        carvers[index].Carve(context, random, startChunkX, startChunkZ);
                }
            }
        }
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

        return type switch
        {
            "minecraft:cave" => new ConfiguredCarver<CaveCarverConfiguration>(new CaveWorldCarver(), new CaveCarverConfiguration
            {
                Probability = probability,
                Y = y,
                YScale = yScale,
                LavaLevel = lavaLevel,
                Replaceable = replaceable,
                HorizontalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("horizontal_radius_multiplier")),
                VerticalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("vertical_radius_multiplier")),
                FloorLevel = FloatProvider.Parse(config.GetProperty("floor_level"))
            }),
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

    private static IReadOnlySet<int> ResolveBlockTag(string tag)
    {
        var name = tag.TrimStart('#').Replace("minecraft:", string.Empty);
        var match = TagsRegistry.Block.All.FirstOrDefault(blockTag => blockTag.Name == name)
            ?? throw new InvalidOperationException($"Unknown block tag '{tag}'.");

        return match.Entries.ToHashSet();
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
