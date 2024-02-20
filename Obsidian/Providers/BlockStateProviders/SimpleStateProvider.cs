using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class SimpleStateProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:simple_state_provider";

    public required SimpleBlockState State { get; init; }

    public IBlock Get() => BlocksRegistry.GetFromSimpleState(this.State);
    public SimpleBlockState GetSimple() => this.State;
}
