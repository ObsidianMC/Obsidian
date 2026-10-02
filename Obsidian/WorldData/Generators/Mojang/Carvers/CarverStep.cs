using Obsidian.API.World.Generator.RandomSources;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// The carvers step of vanilla's NoiseBasedChunkGenerator: runs every carver started within 8 chunks.
/// </summary>
internal sealed class CarverStep
{
    private const int Radius = 8;

    // Kept plans cover this many start chunks along X and Z per carver; see GetPlan.
    private const int PlanWindow = 64;

    private static readonly ConcurrentDictionary<string, IConfiguredCarver> loadedCarvers = new();

    private readonly RandomState randomState;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IBiomeSource biomeSource;
    private readonly IConfiguredCarver[] carvers;
    private readonly PlanEntry?[] plans;

    /// <param name="carvers">Configured carver names in the biomes' order, which decides each carver's seed.</param>
    public CarverStep(RandomState randomState, SurfaceBuilder surfaceBuilder, IBiomeSource biomeSource, IEnumerable<string> carvers)
    {
        this.randomState = randomState;
        this.surfaceBuilder = surfaceBuilder;
        this.biomeSource = biomeSource;
        this.carvers = [.. carvers.Select(name => loadedCarvers.GetOrAdd(name, Load))];
        this.plans = new PlanEntry?[this.carvers.Length * PlanWindow * PlanWindow];
    }

    /// <param name="noiseChunk">The chunk's noise chunk when the other steps share it, or <c>null</c> for a new one.</param>
    public void Apply(IChunk chunk, ICollection<Vector>? fluidUpdates = null, NoiseChunk? noiseChunk = null)
    {
        var settings = this.randomState.Settings;
        noiseChunk ??= new NoiseChunk(this.randomState, chunk.X, chunk.Z);
        var context = new CarvingContext(this.surfaceBuilder, settings.Noise.MinY, settings.Noise.Height)
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
                        carvers[index].Carve(context, this.GetPlan(index, startChunkX, startChunkZ, random));
                }
            }
        }
    }

    /// <summary>
    /// The plan of carver <paramref name="index"/> started in a chunk, made with <paramref name="random"/> if it isn't kept.
    /// </summary>
    /// <remarks>
    /// A start chunk's carvers reach every chunk within 8 chunks, so plans are kept rather than made again for each of them.
    /// Each carver keeps one plan per start chunk in a 64 by 64 window, which covers the start chunks of the chunks generated
    /// around a player; a start chunk a window away takes the slot over. Plans don't change once made, so threads can share them.
    /// </remarks>
    private object GetPlan(int index, int startChunkX, int startChunkZ, IRandomSource random)
    {
        var slot = (index * PlanWindow + (startChunkX & (PlanWindow - 1))) * PlanWindow + (startChunkZ & (PlanWindow - 1));
        var entry = Volatile.Read(ref this.plans[slot]);
        if (entry is null || entry.ChunkX != startChunkX || entry.ChunkZ != startChunkZ)
        {
            var noise = this.randomState.Settings.Noise;
            entry = new PlanEntry(startChunkX, startChunkZ, this.carvers[index].Plan(random, startChunkX, startChunkZ, noise.MinY, noise.Height));
            Volatile.Write(ref this.plans[slot], entry);
        }

        return entry.Plan;
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
            "minecraft:cave" => Configure(new CaveWorldCarver(), CaveConfiguration()),
            "minecraft:nether_cave" => Configure(new NetherWorldCarver(), CaveConfiguration()),
            "minecraft:canyon" => Configure(new CanyonWorldCarver(), new CanyonCarverConfiguration
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

    private static IConfiguredCarver Configure<TConfiguration, TPlan>(WorldCarver<TConfiguration, TPlan> carver, TConfiguration configuration)
        where TConfiguration : CarverConfiguration where TPlan : notnull => new ConfiguredCarver<TConfiguration, TPlan>(carver, configuration);

    private interface IConfiguredCarver
    {
        public bool IsStartChunk(IRandomSource random);

        public object Plan(IRandomSource random, int startChunkX, int startChunkZ, int minY, int height);

        public void Carve(CarvingContext context, object plan);
    }

    private sealed class ConfiguredCarver<TConfiguration, TPlan> : IConfiguredCarver
        where TConfiguration : CarverConfiguration where TPlan : notnull
    {
        private readonly WorldCarver<TConfiguration, TPlan> carver;
        private readonly TConfiguration configuration;

        public ConfiguredCarver(WorldCarver<TConfiguration, TPlan> carver, TConfiguration configuration)
        {
            this.carver = carver;
            this.configuration = configuration;
        }

        public bool IsStartChunk(IRandomSource random) => this.carver.IsStartChunk(this.configuration, random);

        public object Plan(IRandomSource random, int startChunkX, int startChunkZ, int minY, int height) =>
            this.carver.Plan(this.configuration, random, startChunkX, startChunkZ, minY, height);

        public void Carve(CarvingContext context, object plan) => this.carver.Carve(context, this.configuration, (TPlan)plan);
    }

    // A kept plan and the start chunk it belongs to.
    private sealed record PlanEntry(int ChunkX, int ChunkZ, object Plan);
}
