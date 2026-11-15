using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;
using System.Diagnostics.CodeAnalysis;

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
    // A noise the server already seeded for a world from data of another schema, such as a 26.3 noise, which this
    // samples instead of its own parameters; null for a noise of these parameters.
    private readonly INoise? seeded;

    /// <summary>
    /// Initializes unbound noise parameters to be supplied by the caller.
    /// </summary>
    public BaseNoise()
    {
    }

    /// <summary>A noise the server seeded for a world, under its registry key, sampled as it is.</summary>
    [SetsRequiredMembers]
    internal BaseNoise(string key, INoise seeded)
    {
        this.Key = key;
        this.Amplitudes = [];
        this.FirstOctave = 0;
        this.seeded = seeded;
    }

    /// <summary>
    /// Gets the type identifier for registry-backed base noise.
    /// </summary>
    public string Type => "minecraft:base_noise";

    /// <summary>
    /// Registry key (e.g. <c>minecraft:temperature</c>). Vanilla seeds each noise from a hash of this key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the amplitude of each successive octave, read from <c>amplitudes</c>.
    /// </summary>
    public required ImmutableArray<double> Amplitudes { get; init; }

    /// <summary>
    /// Gets the initial octave index, read from <c>firstOctave</c> and converted to an integer when binding.
    /// </summary>
    public required double FirstOctave { get; init; }

    /// <summary>
    /// Gets the negative of the reported maximum noise value.
    /// </summary>
    public double MinValue => -this.MaxValue;

    /// <summary>
    /// Gets the upper bound of the bound noise.
    /// </summary>
    public double MaxValue => this.seeded?.MaxValue ?? this.Noise.MaxValue;

    private NormalNoise? noise;

    private NormalNoise Noise
    {
        get
        {
            if (this.seeded is not null)
                throw new InvalidOperationException($"{this.Key} was seeded from another schema, not a NormalNoise.");

            if (this.noise is null)
                this.Create();

            return this.noise!;
        }
    }

    /// <summary>
    /// Returns a copy of this noise seeded from <paramref name="random"/>; a noise the server seeded already is
    /// returned as it is.
    /// </summary>
    public BaseNoise Bind(IRandomSource random)
    {
        if (this.seeded is not null)
            return this;

        return this.Bind(NormalNoise.Create(random, (int)this.FirstOctave, this.Amplitudes.AsSpan()));
    }

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
    /// Returns a copy of this noise seeded the way vanilla seeds registry noises for a world; a noise the server
    /// seeded already is returned as it is.
    /// </summary>
    public BaseNoise Bind(IPositionalRandomFactory worldRandom) =>
        this.seeded is not null ? this : this.Bind(worldRandom.FromHashOf(this.Key));

    /// <summary>
    /// Binds uninitialized noise using world seed zero and its registry key; does nothing if already bound.
    /// </summary>
    public void Create()
    {
        if (this.seeded is not null || this.noise is not null)
            return;

        var random = new XoroshiroRandomSource(0L).ForkPositional().FromHashOf(this.Key);
        this.noise = NormalNoise.Create(random, (int)this.FirstOctave, this.Amplitudes.AsSpan());
    }

    /// <summary>
    /// Samples the bound noise at the supplied coordinates, binding with world seed zero if necessary.
    /// </summary>
    public double GetValue(double x, double y, double z) =>
        this.seeded is null ? this.Noise.GetValue(x, y, z) : this.seeded.GetValue(x, y, z);

    /// <summary>The noise <see cref="GetValue"/> samples.</summary>
    internal NormalNoise NormalNoise => this.Noise;
}
