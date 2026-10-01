using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// Matches any state of one block, with a probability (sampled only when the block matches).
/// </summary>
[ConfiguredFeatureProperty("minecraft:random_block_match")]
public sealed class RandomBlockMatchTest : IRuleTest
{
    public string Type { get; init; } = "minecraft:random_block_match";

    public required string Block { get; init; }

    public required float Probability { get; init; }

    private int RegistryId => field == 0 ? field = BlocksRegistry.Get(this.Block).RegistryId : field;

    public bool Test(IBlock block, IRandomSource random) => block.RegistryId == this.RegistryId && random.NextFloat() < this.Probability;
}
