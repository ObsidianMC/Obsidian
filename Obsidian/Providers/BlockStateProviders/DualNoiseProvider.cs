namespace Obsidian.Providers.BlockStateProviders;
public sealed class DualNoiseProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:dual_noise_provider";

    public required long Seed { get; set; }

    //??? WHAT IS THIS FOR
    public object? Noise { get; set; }

    public object? SlowNoise { get; set; }

    public required float SlowScale { get; set; }

    public required float Scale { get; set; }

    public required IntProviderRangeValue Variety { get; set; }

    public List<IBlock> States { get; } = [];
}
