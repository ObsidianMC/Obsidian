using Obsidian.API.Noise;
using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using System.Collections.Concurrent;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Per-world seeded view of a <see cref="NoiseSetting"/>: the noise router with every noise bound to the
/// world seed, plus the positional randoms used by aquifers, ore veins and surface rules.
/// </summary>
/// <remarks>
/// Mirrors vanilla's RandomState. Thread-safe once constructed; the router is immutable and shared by all chunks.
/// </remarks>
internal sealed class RandomState
{
    private readonly ConcurrentDictionary<string, BaseNoise> noises = new();
    private readonly ConcurrentDictionary<string, IPositionalRandomFactory> positionalRandoms = new();
    private readonly NoiseWiringVisitor wiring;

    public long Seed { get; }

    public NoiseSetting Settings { get; }

    /// <summary>
    /// The settings' noise router with seeded noises. Marker functions are kept; evaluating them directly
    /// returns raw values, which is what vanilla's climate sampler uses.
    /// </summary>
    public NoiseRouter Router { get; }

    /// <summary>
    /// Positional random factory seeded from the world seed; every other worldgen random derives from it.
    /// </summary>
    public IPositionalRandomFactory Random { get; }

    public IPositionalRandomFactory AquiferRandom { get; }

    public IPositionalRandomFactory OreRandom { get; }

    public RandomState(NoiseSetting settings, long seed)
    {
        this.Seed = seed;
        this.Settings = settings;

        IRandomSource worldRandom = settings.LegacyRandomSource ? new LegacyRandomSource(seed) : new XoroshiroRandomSource(seed);
        this.Random = worldRandom.ForkPositional();
        this.AquiferRandom = this.Random.FromHashOf("minecraft:aquifer").ForkPositional();
        this.OreRandom = this.Random.FromHashOf("minecraft:ore").ForkPositional();

        this.wiring = new NoiseWiringVisitor(this);
        var router = settings.NoiseRouter;

        this.Router = new NoiseRouter
        {
            Barrier = this.wiring.Map(router.Barrier),
            Continents = this.wiring.Map(router.Continents),
            Depth = this.wiring.Map(router.Depth),
            Erosion = this.wiring.Map(router.Erosion),
            FinalDensity = this.wiring.Map(router.FinalDensity),
            FluidLevelFloodedness = this.wiring.Map(router.FluidLevelFloodedness),
            FluidLevelSpread = this.wiring.Map(router.FluidLevelSpread),
            PreliminarySurfaceLevel = this.wiring.Map(router.PreliminarySurfaceLevel),
            Lava = this.wiring.Map(router.Lava),
            Ridges = this.wiring.Map(router.Ridges),
            Temperature = this.wiring.Map(router.Temperature),
            Vegetation = this.wiring.Map(router.Vegetation),
            VeinGap = this.wiring.Map(router.VeinGap),
            VeinRidged = this.wiring.Map(router.VeinRidged),
            VeinToggle = this.wiring.Map(router.VeinToggle)
        };
    }

    /// <summary>
    /// Binds the noises of any density function (e.g. a registry entry) to this world's seed.
    /// </summary>
    public IDensityFunction Bind(IDensityFunction function) => this.wiring.Map(function);

    /// <summary>
    /// Gets the seeded instance of a registry noise, e.g. <c>minecraft:surface</c>.
    /// </summary>
    public BaseNoise GetOrCreateNoise(string key) =>
        this.noises.GetOrAdd(key, static (key, random) => NoiseRegistry.Noises.All[key].Bind(random), this.Random);

    /// <summary>
    /// Gets a positional random factory derived from the world seed and <paramref name="name"/>.
    /// </summary>
    public IPositionalRandomFactory GetOrCreateRandomFactory(string name) =>
        this.positionalRandoms.GetOrAdd(name, static (name, random) => random.FromHashOf(name).ForkPositional(), this.Random);

    /// <summary>
    /// Parses a level seed the way vanilla does: a number is used as-is, any other text uses Java's String.hashCode.
    /// </summary>
    public static long ParseSeed(string seed)
    {
        seed = seed.Trim();

        if (long.TryParse(seed, out var numericSeed))
            return numericSeed;

        var hash = 0;
        foreach (var c in seed)
            hash = unchecked(31 * hash + c);

        return hash;
    }

    private sealed class NoiseWiringVisitor : IDensityFunctionVisitor
    {
        private readonly RandomState state;
        private readonly bool legacy;

        // Keeps subtrees that are shared in the registry shared in the seeded router.
        private readonly ConcurrentDictionary<IDensityFunction, IDensityFunction> mapped = new(ReferenceEqualityComparer.Instance);

        public NoiseWiringVisitor(RandomState state)
        {
            this.state = state;
            this.legacy = state.Settings.LegacyRandomSource;
        }


        public IDensityFunction Map(IDensityFunction function) =>
            this.mapped.TryGetValue(function, out var result) ? result : this.mapped.GetOrAdd(function, function.MapAll(this));

        public INoise VisitNoise(INoise noise)
        {
            if (noise is not BaseNoise baseNoise)
                return noise;

            if (this.legacy)
            {
                // Legacy settings (nether, end) keep the pre-1.18 seeding for these noises.
                switch (baseNoise.Key)
                {
                    case "minecraft:temperature":
                        return baseNoise.Bind(NormalNoise.CreateLegacyNetherBiome(new LegacyRandomSource(this.state.Seed), -7, [1.0, 1.0]));
                    case "minecraft:vegetation":
                        return baseNoise.Bind(NormalNoise.CreateLegacyNetherBiome(new LegacyRandomSource(this.state.Seed + 1L), -7, [1.0, 1.0]));
                    case "minecraft:offset":
                        return baseNoise.Bind(NormalNoise.Create(this.state.Random.FromHashOf("minecraft:offset"), 0, [0.0]));
                }
            }

            return this.state.GetOrCreateNoise(baseNoise.Key);
        }

        public IDensityFunction Apply(IDensityFunction function) => function switch
        {
            OldBlendedNoiseDensityFunction blended => blended.WithRandom(this.legacy
                ? new LegacyRandomSource(this.state.Seed)
                : this.state.Random.FromHashOf("minecraft:terrain")),
            EndIslandsDensityFunction => EndIslandsDensityFunction.WithSeed(this.state.Seed),
            _ => function
        };
    }
}
