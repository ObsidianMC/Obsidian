using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class SimpleStateProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:simple_state_provider";

    public IBlock Get(string blockIdentifier, IBlockState state) =>
        BlocksRegistry.Get(blockIdentifier, state);
}
