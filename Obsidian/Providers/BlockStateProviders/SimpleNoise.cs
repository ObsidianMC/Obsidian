namespace Obsidian.Providers.BlockStateProviders;
public sealed class SimpleNoise
{
    public required double[] Amplitudes { get; init; }

    public required int FirstOctave { get; init; }
}
