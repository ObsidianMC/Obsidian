using Obsidian.API.Noise;
using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

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
    private readonly ThreadLocal<ClimateSampler> climateSamplers;

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

    /// <summary>
    /// The fluid at a position when no aquifer applies: lava deep down, the default fluid up to sea level.
    /// </summary>
    public FluidPicker GlobalFluidPicker { get; }

    public RandomState(NoiseSetting settings, long seed)
    {
        this.Seed = seed;
        this.Settings = settings;

        IRandomSource worldRandom = settings.LegacyRandomSource ? new LegacyRandomSource(seed) : new XoroshiroRandomSource(seed);
        this.Random = worldRandom.ForkPositional();
        this.AquiferRandom = this.Random.FromHashOf("minecraft:aquifer").ForkPositional();
        this.OreRandom = this.Random.FromHashOf("minecraft:ore").ForkPositional();
        this.GlobalFluidPicker = Aquifers.CreateGlobalFluidPicker(settings.SeaLevel, BlocksRegistry.GetFromSimpleState(settings.DefaultFluid));

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

        this.climateSamplers = new ThreadLocal<ClimateSampler>(this.CreateClimateSampler);
    }

    /// <summary>
    /// Router functions referenced from more than one place, other than markers and trivial functions. Such a function
    /// tends to be sampled several times per position (vanilla's overworld sloped cheese up to three times per cell
    /// corner), so chunks remember its last value.
    /// </summary>
    public IReadOnlySet<IDensityFunction> SharedFunctions => field ??= this.FindSharedFunctions();

    /// <summary>
    /// A climate sampler over the router for the calling thread, like vanilla's <c>RandomState.sampler()</c>. Its markers
    /// and shared functions remember their last sampled position, since the depth's splines sample continentalness,
    /// erosion and ridges many times per position and the climate point samples them again. Don't hand it to other
    /// threads.
    /// </summary>
    public ClimateSampler ClimateSampler => this.climateSamplers.Value!;

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

    private ClimateSampler CreateClimateSampler()
    {
        var visitor = new LastPositionCaching(this.SharedFunctions);
        var router = this.Router;
        return new ClimateSampler(visitor.Map(router.Temperature), visitor.Map(router.Vegetation), visitor.Map(router.Continents),
            visitor.Map(router.Erosion), visitor.Map(router.Depth), visitor.Map(router.Ridges));
    }

    private HashSet<IDensityFunction> FindSharedFunctions()
    {
        var counter = new ReferenceCounter();
        var router = this.Router;
        IDensityFunction[] roots =
        [
            router.Barrier, router.Continents, router.Depth, router.Erosion, router.FinalDensity, router.FluidLevelFloodedness,
            router.FluidLevelSpread, router.PreliminarySurfaceLevel, router.Lava, router.Ridges, router.Temperature,
            router.Vegetation, router.VeinGap, router.VeinRidged, router.VeinToggle
        ];

        foreach (var root in roots)
            counter.Map(root);

        return counter.Counts
            .Where(entry => entry.Value > 1 && entry.Key is not (ConstantDensityFunction or YClampedGradientDensityFunction
                or BlendAlphaDensityFunction or BlendOffsetDensityFunction or InterpolatedDensityFunction or FlatCacheDensityFunction
                or Cache2DDensityFunction or CacheOnceDensityFunction))
            .Select(entry => entry.Key)
            .ToHashSet<IDensityFunction>(ReferenceEqualityComparer.Instance);
    }

    private sealed class NoiseWiringVisitor : IDensityFunctionVisitor
    {
        private readonly RandomState state;
        private readonly bool legacy;

        // Keeps subtrees that are shared in the registry shared in the seeded router.
        private readonly ConcurrentDictionary<IDensityFunction, IDensityFunction> mapped = new(ReferenceEqualityComparer.Instance);

        // Canonical instances by type, argument (by reference) and parameters; see Canonicalize.
        private readonly ConcurrentDictionary<CanonicalKey, IDensityFunction> canonical = new();

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
            _ => this.Canonicalize(function)
        };

        /// <summary>
        /// Returns the first seen instance equal to <paramref name="function"/>, for noises and markers over the same
        /// (already canonical) argument. Registry entries spell out some subtrees twice; vanilla's overworld cave entrances
        /// sample the same cached rarity noise through two separate <c>cache_once</c> markers. Sharing the instance
        /// shares the chunk's cache, and these functions are pure, so values don't change.
        /// </summary>
        private IDensityFunction Canonicalize(IDensityFunction function)
        {
            CanonicalKey? key = function switch
            {
                NoiseDensityFunction noise => new CanonicalKey(typeof(NoiseDensityFunction), noise.Noise,
                    BitConverter.DoubleToInt64Bits(noise.XzScale), BitConverter.DoubleToInt64Bits(noise.YScale)),
                CacheOnceDensityFunction cacheOnce => new CanonicalKey(typeof(CacheOnceDensityFunction), cacheOnce.Argument, 0L, 0L),
                Cache2DDensityFunction cache2D => new CanonicalKey(typeof(Cache2DDensityFunction), cache2D.Argument, 0L, 0L),
                FlatCacheDensityFunction flatCache => new CanonicalKey(typeof(FlatCacheDensityFunction), flatCache.Argument, 0L, 0L),
                InterpolatedDensityFunction interpolated => new CanonicalKey(typeof(InterpolatedDensityFunction), interpolated.Argument, 0L, 0L),
                _ => null
            };

            return key is null ? function : this.canonical.GetOrAdd(key.Value, function);
        }

        private readonly record struct CanonicalKey(Type Type, object Argument, long First, long Second)
        {
            public bool Equals(CanonicalKey other) => this.Type == other.Type && ReferenceEquals(this.Argument, other.Argument)
                && this.First == other.First && this.Second == other.Second;

            public override int GetHashCode() => HashCode.Combine(this.Type, RuntimeHelpers.GetHashCode(this.Argument), this.First, this.Second);
        }
    }

    /// <summary>
    /// Copies a tree, putting a <see cref="CacheOnce"/> over its markers (which evaluate their argument directly here) and its
    /// shared functions.
    /// </summary>
    private sealed class LastPositionCaching(IReadOnlySet<IDensityFunction> sharedFunctions) : IDensityFunctionVisitor
    {
        private readonly Dictionary<IDensityFunction, IDensityFunction> mapped = new(ReferenceEqualityComparer.Instance);

        public IDensityFunction Map(IDensityFunction function)
        {
            if (!this.mapped.TryGetValue(function, out var result))
            {
                result = function.MapAll(this);
                if (sharedFunctions.Contains(function) || function is FlatCacheDensityFunction or Cache2DDensityFunction
                    or CacheOnceDensityFunction)
                    result = new CacheOnce(result);

                this.mapped[function] = result;
            }

            return result;
        }

        public IDensityFunction Apply(IDensityFunction function) => function;
    }

    /// <summary>
    /// Counts how often each function of a tree is referenced, visiting shared subtrees once.
    /// </summary>
    private sealed class ReferenceCounter : IDensityFunctionVisitor
    {
        public Dictionary<IDensityFunction, int> Counts { get; } = new(ReferenceEqualityComparer.Instance);

        public IDensityFunction Map(IDensityFunction function)
        {
            this.Counts[function] = this.Counts.GetValueOrDefault(function) + 1;
            if (this.Counts[function] == 1)
                function.MapAll(this);

            return function;
        }

        public IDensityFunction Apply(IDensityFunction function) => function;
    }
}
