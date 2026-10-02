using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// Matches any state of one block.
/// </summary>
[ConfiguredFeatureProperty("minecraft:block_match")]
public sealed class BlockMatchTest : IStateRuleTest
{
    public string Type { get; init; } = "minecraft:block_match";

    public required string Block { get; init; }

    private int RegistryId => field == 0 ? field = BlocksRegistry.Get(this.Block).RegistryId : field;

    public bool Test(IBlock block, IRandomSource random) => block.RegistryId == this.RegistryId;

    public bool Test(int stateId, IRandomSource random) => BlocksRegistry.RegistryIdOf(stateId) == this.RegistryId;
}
