using Obsidian.Registries;

namespace Obsidian.BlockStateProviders;
public sealed class SimpleStateProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:simple_state_provider";

    public IBlock Get(string blockIndentifier, IReadOnlyDictionary<string, object> properties)
    {

        return BlocksRegistry.Air;
    }
}
