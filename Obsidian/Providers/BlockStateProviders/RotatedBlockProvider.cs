using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;
public sealed class RotatedBlockProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:rotated_block_provider";

    public required SimpleBlockState State { get; init; }

    public IBlock Get() => BlocksRegistry.GetFromSimpleState(this.State);

    public SimpleBlockState GetSimple() => this.State;
}
