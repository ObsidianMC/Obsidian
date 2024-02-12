using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class RotatedBlockProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:rotated_block_provider";

    public IBlock Get(string blockIdentifier) => BlocksRegistry.Get(blockIdentifier);
}
