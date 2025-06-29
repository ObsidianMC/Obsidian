using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;

[TreeProperty("minecraft:noise_threshold_provider")]
public sealed class NoiseThresholdProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:noise_threshold_provider";

    public required long Seed { get; set; }

    public SimpleNoise? Noise { get; set; }

    public required float Scale { get; set; }

    public required float Threshold { get; set; }

    public required float HighChance { get; set; }

    public required SimpleBlockState DefaultState { get; set; }

    public List<SimpleBlockState> LowStates { get; } = [];

    public List<SimpleBlockState> HighStates { get; } = [];

    public IBlock Get()
    {
        return BlocksRegistry.Air;
    }

    public SimpleBlockState GetSimple()
    {
        return new() { Name = BlocksRegistry.Air.UnlocalizedName };
    }
}
