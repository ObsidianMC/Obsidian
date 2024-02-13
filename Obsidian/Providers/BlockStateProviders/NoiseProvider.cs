using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class NoiseProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:noise_provider";

    public required long Seed { get; set; }

    //??? WHAT IS THIS FOR
    public object? Noise { get; set; }

    public required float Scale { get; set; }

    public List<IBlock> States { get; } = [];
}
