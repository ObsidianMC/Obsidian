namespace Obsidian.Providers.BlockStateProviders;
public sealed class NoiseThresholdProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:noise_threshold_provider";

    public required long Seed { get; set; }

    //??? WHAT IS THIS FOR
    public object? Noise { get; set; }

    public required float Scale { get; set; }

    public required float Threshold { get; set; }

    public required float HighChance { get; set; }

    public required IBlock DefaultState { get; set; }

    public List<IBlock> LowStates { get; } = [];

    public List<IBlock> HighStates { get; } = [];
}
