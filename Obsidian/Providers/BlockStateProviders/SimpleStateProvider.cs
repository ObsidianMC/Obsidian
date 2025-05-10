using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;

[TreeProperty("minecraft:simple_state_provider")]
public sealed class SimpleStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:simple_state_provider";

    public required SimpleBlockState State { get; init; }

    public IBlock Get() => BlocksRegistry.GetFromSimpleState(this.State);
    public SimpleBlockState GetSimple() => this.State;
}
