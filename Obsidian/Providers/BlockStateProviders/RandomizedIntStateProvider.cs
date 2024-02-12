namespace Obsidian.Providers.BlockStateProviders;
public sealed class RandomizedIntStateProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:randomized_int_state_provider";

    public string Property { get; set; } = default!;
}
