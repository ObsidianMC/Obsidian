namespace Obsidian.Providers.BlockStateProviders;
public sealed class WeightedStateProvider : IBlockStateProvider
{
    public string Identifier => "minecraft:weighted_state_provider";

    public List<WeightedStateEntry> Entries { get; } = [];

    public void AddEntry(IBlock block, int weight) => Entries.Add(new()
    {
        Data = block,
        Weight = weight
    });

    public readonly struct WeightedStateEntry
    {
        public required IBlock Data { get; init; }

        public required int Weight { get; init; }
    }
}
