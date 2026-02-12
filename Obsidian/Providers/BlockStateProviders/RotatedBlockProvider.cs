namespace Obsidian.Providers.BlockStateProviders;

[ConfiguredFeatureProperty("minecraft:rotated_block_provider")]
public sealed class RotatedBlockProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:rotated_block_provider";

    public required SimpleBlockState State { get; init; }

    public IBlock Get() => BlocksRegistry.GetFromSimpleState(this.State);

    public SimpleBlockState GetSimple() => this.State;
}
