using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks against vanilla 1.21.11. Every expected value was produced by running the named vanilla call
/// against the remapped client jar; doubles are compared exactly.
/// </summary>
public class VanillaRandomAndNoise
{
    [Fact(DisplayName = "Xoroshiro sequence matches vanilla")]
    public void XoroshiroSequence()
    {
        // new XoroshiroRandomSource(12345L), calls in this order
        var random = new XoroshiroRandomSource(12345L);

        Assert.Equal(-8118485274630516485L, random.NextLong());
        Assert.Equal(8241557746459281790L, random.NextLong());
        Assert.Equal(-527878333, random.NextInt());
        Assert.Equal(1, random.NextInt(100));
        Assert.Equal(8, random.NextInt(16));
        Assert.Equal(1385770128, random.NextInt(2000000011));
        Assert.Equal(7, random.NextInt(5, 17));
        Assert.Equal(0, random.NextIntBetweenInclusive(-3, 3));
        Assert.Equal(0.59923637f, random.NextFloat());
        Assert.Equal(0.33121533598371833, random.NextDouble());
        Assert.False(random.NextBoolean());
        Assert.Equal(-1.8888701858134413, random.NextGaussian());
        Assert.Equal(-0.15987747609493008, random.NextGaussian());
        Assert.Equal(0.5716420411563893, random.NextGaussian());
        random.ConsumeCount(5);
        Assert.Equal(-3328935150948411374L, random.NextLong());
        Assert.Equal(1.850551111307698, random.Triangle(1.0, 2.0));
        Assert.Equal(7330507231386189387L, random.Fork().NextLong());
        Assert.Equal(6901483983031054675L, random.NextLong());

        // new XoroshiroRandomSource(0L, 0L).nextLong() (all-zero state falls back to golden/silver ratios)
        Assert.Equal(6807859099481836695L, new XoroshiroRandomSource(0L, 0L).NextLong());

        // new XoroshiroRandomSource(1L); nextGaussian(); setSeed(777L); nextGaussian() (setSeed drops the cached gaussian)
        var reseeded = new XoroshiroRandomSource(1L);
        reseeded.NextGaussian();
        reseeded.SetSeed(777L);
        Assert.Equal(-0.6975664699734704, reseeded.NextGaussian());
    }

    [Fact(DisplayName = "Legacy sequence matches vanilla")]
    public void LegacySequence()
    {
        // new LegacyRandomSource(12345L), calls in this order
        var random = new LegacyRandomSource(12345L);

        Assert.Equal(6674089274190705457L, random.NextLong());
        Assert.Equal(-1236052134575208584L, random.NextLong());
        Assert.Equal(-716867186, random.NextInt());
        Assert.Equal(84, random.NextInt(100));
        Assert.Equal(5, random.NextInt(16));
        Assert.Equal(267722802, random.NextInt(2000000011));
        Assert.Equal(6, random.NextInt(5, 17));
        Assert.Equal(-2, random.NextIntBetweenInclusive(-3, 3));
        Assert.Equal(0.3491153f, random.NextFloat());
        Assert.Equal(0.9880507953178529, random.NextDouble());
        Assert.True(random.NextBoolean());
        Assert.Equal(0.4139063973267458, random.NextGaussian());
        Assert.Equal(-1.0238338759685135, random.NextGaussian());
        Assert.Equal(-0.2725215573225036, random.NextGaussian());
        random.ConsumeCount(5);
        Assert.Equal(-1223267609624301856L, random.NextLong());
        Assert.Equal(1.8873806730695184, random.Triangle(1.0, 2.0));
        Assert.Equal(1501632028650852685L, random.Fork().NextLong());
        Assert.Equal(2973996490872454669L, random.NextLong());
    }

    [Fact(DisplayName = "RandomSupport seeds match vanilla")]
    public void RandomSupportSeeds()
    {
        // RandomSupport.mixStafford13(...)
        Assert.Equal(-906084347102765743L, RandomSupport.MixStafford13(12345L));
        Assert.Equal(-5417735806833148549L, RandomSupport.MixStafford13(-1L));

        // RandomSupport.upgradeSeedTo128bit(12345L)
        Assert.Equal(new Seed128Bit(733019005196230046L, -3494074583369400597L), RandomSupport.UpgradeSeedTo128Bit(12345L));

        // RandomSupport.seedFromHashOf(...)
        Assert.Equal(new Seed128Bit(577895406318539652L, 4557074653038767061L), RandomSupport.SeedFromHashOf("minecraft:offset"));
        Assert.Equal(new Seed128Bit(6040343492819601496L, -4840870798921671874L), RandomSupport.SeedFromHashOf("octave_-3"));
    }

    [Fact(DisplayName = "Positional random factories match vanilla")]
    public void PositionalFactories()
    {
        // new XoroshiroRandomSource(42L).forkPositional()
        var xoroshiro = new XoroshiroRandomSource(42L).ForkPositional();
        Assert.Equal(5350271449351521578L, xoroshiro.FromHashOf("minecraft:terrain").NextLong());
        Assert.Equal(-3901958155660295472L, xoroshiro.At(1, -2, 3).NextLong());
        Assert.Equal(-706013204, xoroshiro.At(-30000000, 320, 29999999).NextInt());
        Assert.Equal(726233142558335723L, xoroshiro.FromSeed(99L).NextLong());

        // new LegacyRandomSource(42L).forkPositional()
        var legacy = new LegacyRandomSource(42L).ForkPositional();
        Assert.Equal(-4127464446246398199L, legacy.FromHashOf("minecraft:terrain").NextLong());
        Assert.Equal(-7433459726332581829L, legacy.At(1, -2, 3).NextLong());
        Assert.Equal(-5119754439980850796L, legacy.FromSeed(99L).NextLong());
    }

    [Fact(DisplayName = "WorldgenRandom seeding matches vanilla")]
    public void WorldgenRandomSeeds()
    {
        // new WorldgenRandom(new LegacyRandomSource(0L)), calls in this order
        var legacy = new WorldgenRandom(new LegacyRandomSource(0L));
        var decorationSeed = legacy.SetDecorationSeed(1234L, 16, -32);
        Assert.Equal(-1810219191199069502L, decorationSeed);
        legacy.SetFeatureSeed(decorationSeed, 3, 7);
        Assert.Equal(0, legacy.NextInt(16));
        Assert.Equal(0.47520258057976505, legacy.NextDouble());
        legacy.SetLargeFeatureSeed(1234L, 5, -5);
        Assert.Equal(13, legacy.NextInt(100));
        legacy.SetLargeFeatureWithSalt(1234L, 5, -5, 10387312);
        Assert.Equal(63, legacy.NextInt(100));

        // new WorldgenRandom(new XoroshiroRandomSource(0L)): bits come from the top of nextLong()
        var xoroshiro = new WorldgenRandom(new XoroshiroRandomSource(0L));
        Assert.Equal(-8293496358049190334L, xoroshiro.SetDecorationSeed(1234L, 16, -32));
        Assert.Equal(8, xoroshiro.NextInt(16));
        Assert.Equal(7577678699328881724L, xoroshiro.NextLong());
        Assert.Equal(0.8028506886437642, xoroshiro.NextDouble());
        Assert.Equal(0.64570045f, xoroshiro.NextFloat());
    }

    [Fact(DisplayName = "ImprovedNoise matches vanilla")]
    public void ImprovedNoiseSamples()
    {
        // new ImprovedNoise(new XoroshiroRandomSource(1L))
        var noise = new ImprovedNoise(new XoroshiroRandomSource(1L));

        Assert.Equal(241.65497889541743, noise.Xo);
        Assert.Equal(89.53494281737213, noise.Yo);
        Assert.Equal(230.71619350863378, noise.Zo);
        Assert.Equal(-0.19750132263099207, noise.Noise(0.5, 1.25, -3.75));
        Assert.Equal(0.15941055187870795, noise.Noise(123.456, -78.9, 1000.1));
        Assert.Equal(-0.0985911869958731, noise.Noise(10.3, 20.7, 30.1, 0.5, 0.2));

        var derivatives = new double[3];
        Assert.Equal(0.171277830611118, noise.NoiseWithDerivative(1.1, 2.2, 3.3, derivatives));
        Assert.Equal([-1.7255850195096905, -0.01722882190013697, 0.11406234222773272], derivatives);
    }

    [Fact(DisplayName = "PerlinNoise matches vanilla")]
    public void PerlinNoiseSamples()
    {
        // PerlinNoise.create(new XoroshiroRandomSource(1L), -3, [1.0, 0.0, 0.5, 2.0])
        var modern = PerlinNoise.Create(new XoroshiroRandomSource(1L), -3, [1.0, 0.0, 0.5, 2.0]);
        Assert.Equal(-0.1501524496841374, modern.GetValue(1.5, 2.5, 3.5));
        Assert.Equal(0.02583423053600615, modern.GetValue(-100.2, 64, 3000.7));
        Assert.Equal(0.07624852538295895, modern.GetValue(1e9, -5e8, 3.3e7));
        Assert.Equal(2.566666666666667, modern.MaxBrokenValue(1.5));
        Assert.Equal(181.95569002013397, modern.GetOctaveNoise(0)!.Xo);
        Assert.Equal(1.6445568E7, PerlinNoise.Wrap(5e7));

        // PerlinNoise.createLegacyForBlendedNoise(new LegacyRandomSource(1L), IntStream.of(-7, -5, -1, 0))
        var legacy = PerlinNoise.CreateLegacyForBlendedNoise(new LegacyRandomSource(1L), [-7, -5, -1, 0]);
        Assert.Equal(-0.19549702190965676, legacy.GetValue(1.5, 2.5, 3.5));
        Assert.Equal(-0.1907334496808617, legacy.GetValue(1.5, 2.5, 3.5, 0.25, 0.5, false));
        Assert.Equal(-0.008088153488869794, legacy.GetValue(1.5, 2.5, 3.5, 0, 0, true));
        Assert.Equal(187.10481682004246, legacy.GetOctaveNoise(0)!.Xo);
        Assert.Equal(134.37213130105846, legacy.GetOctaveNoise(7)!.Xo);
        Assert.Null(legacy.GetOctaveNoise(2)); // octave -2 has zero amplitude
    }

    [Fact(DisplayName = "NormalNoise matches vanilla")]
    public void NormalNoiseSamples()
    {
        // NormalNoise.create(new XoroshiroRandomSource(1L), -7, 1.0, 1.0, 0.0, 1.0)
        var modern = NormalNoise.Create(new XoroshiroRandomSource(1L), -7, [1.0, 1.0, 0.0, 1.0]);
        Assert.Equal(0.03992428721806851, modern.GetValue(0, 0, 0));
        Assert.Equal(-0.20410994058377474, modern.GetValue(12.3, -45.6, 789.0));
        Assert.Equal(4.622222222222222, modern.MaxValue);

        // NormalNoise.createLegacyNetherBiome(new LegacyRandomSource(1L), new NoiseParameters(-7, 1.0, 1.0))
        var legacy = NormalNoise.CreateLegacyNetherBiome(new LegacyRandomSource(1L), -7, [1.0, 1.0]);
        Assert.Equal(0.14082137008359874, legacy.GetValue(12.3, -45.6, 789.0));
    }

    [Fact(DisplayName = "SimplexNoise matches vanilla")]
    public void SimplexNoiseSamples()
    {
        // new SimplexNoise(new LegacyRandomSource(1L))
        var noise = new SimplexNoise(new LegacyRandomSource(1L));

        Assert.Equal(187.10481682004246, noise.Xo);
        Assert.Equal(-0.41507486808482547, noise.GetValue(1.3, -2.7));
        Assert.Equal(0.259330534979423, noise.GetValue(10.5, 3.3, -7.1));
        Assert.Equal(-0.7178768936296297, noise.GetValue(-0.2, 0.9, 0.4));
    }

    [Fact(DisplayName = "PerlinSimplexNoise matches vanilla")]
    public void PerlinSimplexNoiseSamples()
    {
        // Biome temperature noise: new PerlinSimplexNoise(new WorldgenRandom(new LegacyRandomSource(1234L)), List.of(0))
        var temperature = new PerlinSimplexNoise(new WorldgenRandom(new LegacyRandomSource(1234L)), [0]);
        Assert.Equal(0.8136667115405001, temperature.GetValue(1.25, -2.5, false));
        Assert.Equal(0.18379635320325083, temperature.GetValue(100.0125, -37.5, false));

        // new PerlinSimplexNoise(new LegacyRandomSource(7L), List.of(-2, 0, 1)) covers negative and positive octaves
        var mixed = new PerlinSimplexNoise(new LegacyRandomSource(7L), [-2, 0, 1]);
        Assert.Equal(0.5112793433795323, mixed.GetValue(3.3, -4.4, true));
        Assert.Equal(-0.3336608901331012, mixed.GetValue(3.3, -4.4, false));
    }

    [Fact(DisplayName = "BlendedNoise matches vanilla")]
    public void BlendedNoiseSamples()
    {
        // BlendedNoise.createUnseeded(0.25, 0.125, 80.0, 160.0, 8.0)
        //     .withNewRandom(new XoroshiroRandomSource(42L).forkPositional().fromHashOf("minecraft:terrain"))
        var terrain = new XoroshiroRandomSource(42L).ForkPositional().FromHashOf("minecraft:terrain");
        var noise = BlendedNoise.CreateUnseeded(0.25, 0.125, 80.0, 160.0, 8.0).WithNewRandom(terrain);

        Assert.Equal(0.2818390698715483, noise.Compute(0, 0, 0));
        Assert.Equal(0.022030740517268665, noise.Compute(100, 64, -200));
        Assert.Equal(-0.15129938936506515, noise.Compute(-37, -40, 1234));
        Assert.Equal(87.55150000000002, noise.MaxValue);

        // BlendedNoise.createUnseeded(1.0, 1.0, 80.0, 160.0, 8.0).compute(5, 10, 15)
        Assert.Equal(-0.09536281991745088, BlendedNoise.CreateUnseeded(1.0, 1.0, 80.0, 160.0, 8.0).Compute(5, 10, 15));
    }
}
