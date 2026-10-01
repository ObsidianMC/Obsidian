using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Inline normal noise settings (<c>{"firstOctave": n, "amplitudes": [...]}</c>), like vanilla's NoiseParameters.
/// </summary>
public sealed class NoiseParameters
{
    public required int FirstOctave { get; init; }

    public required double[] Amplitudes { get; init; }

    /// <summary>
    /// Creates the noise the way noise-based state providers seed it: a legacy random wrapped in a WorldgenRandom.
    /// </summary>
    public NormalNoise Create(long seed) => NormalNoise.Create(new WorldgenRandom(new LegacyRandomSource(seed)), this.FirstOctave, this.Amplitudes);
}
