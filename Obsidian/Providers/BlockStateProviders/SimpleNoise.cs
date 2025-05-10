namespace Obsidian.Providers.BlockStateProviders;
public sealed class SimpleNoise
{
    public required float[] Amplitudes { get; init; }

    public required int FirstOctave { get; init; }
}
