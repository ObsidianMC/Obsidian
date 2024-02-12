using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class NoiseProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:noise_provider";
}
