using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Generator.Noise;

/// <summary>
/// A worldgen noise from the <c>worldgen/noise</c> registry, backed by vanilla's <see cref="NormalNoise"/>.
/// </summary>
/// <remarks>
/// Registry instances are unseeded. World generation binds a seeded copy per world with <see cref="Bind(IPositionalRandomFactory)"/>;
/// sampling an unbound instance lazily binds it as if the world seed were 0.
/// </remarks>
public partial class BaseNoise : INoise
{
    public string Type => "minecraft:base_noise";

    /// <summary>
    /// Registry key (e.g. <c>minecraft:temperature</c>). Vanilla seeds each noise from a hash of this key.
    /// </summary>
    public required string Key { get; init; }

    public required double[] Amplitudes { get; init; }

    public required double FirstOctave { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Noise.MaxValue;

    private NormalNoise? noise;

    private NormalNoise Noise
    {
        get
        {
            if (this.noise is null)
                this.Create();

            return this.noise!;
        }
    }

    /// <summary>
    /// Returns a copy of this noise seeded from <paramref name="random"/>.
    /// </summary>
    public BaseNoise Bind(IRandomSource random) => this.Bind(NormalNoise.Create(random, (int)this.FirstOctave, this.Amplitudes));

    /// <summary>
    /// Returns a copy of this noise backed by <paramref name="noise"/>. Used for vanilla's legacy special cases,
    /// where the sampled noise doesn't match the registry parameters.
    /// </summary>
    public BaseNoise Bind(NormalNoise noise) => new()
    {
        Key = this.Key,
        Amplitudes = this.Amplitudes,
        FirstOctave = this.FirstOctave,
        noise = noise
    };

    /// <summary>
    /// Returns a copy of this noise seeded the way vanilla seeds registry noises for a world.
    /// </summary>
    public BaseNoise Bind(IPositionalRandomFactory worldRandom) => this.Bind(worldRandom.FromHashOf(this.Key));

    public void Create() =>
        this.noise ??= NormalNoise.Create(new XoroshiroRandomSource(0L).ForkPositional().FromHashOf(this.Key), (int)this.FirstOctave, this.Amplitudes);

    public double GetValue(double x, double y, double z) => this.Noise.GetValue(x, y, z);
}
