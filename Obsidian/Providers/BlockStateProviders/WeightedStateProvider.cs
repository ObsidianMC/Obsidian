using Obsidian.Registries;

namespace Obsidian.Providers.BlockStateProviders;

[TreeProperty("minecraft:weighted_state_provider")]
public sealed class WeightedStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:weighted_state_provider";

    public List<WeightedStateEntry> Entries { get; set; } = [];

    public void AddEntry(IBlock block, int weight) => Entries.Add(new()
    {
        Data = new() { Name = block.UnlocalizedName },
        Weight = weight
    });

    public readonly struct WeightedStateEntry
    {
        public required SimpleBlockState Data { get; init; }

        public required int Weight { get; init; }
    }

    public IBlock Get() =>
        BlocksRegistry.GetFromSimpleState(this.Entries.OrderBy(x => x.Weight).First().Data);
    public SimpleBlockState GetSimple() =>
         this.Entries.OrderBy(x => x.Weight).First().Data;
}
