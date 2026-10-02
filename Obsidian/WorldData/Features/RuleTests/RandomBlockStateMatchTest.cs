using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// Matches one exact block state, with a probability (sampled only when the state matches).
/// </summary>
[ConfiguredFeatureProperty("minecraft:random_blockstate_match")]
public sealed class RandomBlockStateMatchTest : IStateRuleTest
{
    public string Type { get; init; } = "minecraft:random_blockstate_match";

    public required SimpleBlockState BlockState { get; init; }

    public required float Probability { get; init; }

    private IBlock Expected => field ??= BlocksRegistry.GetFromSimpleState(this.BlockState);

    public bool Test(IBlock block, IRandomSource random) => block.IsSameState(this.Expected) && random.NextFloat() < this.Probability;

    public bool Test(int stateId, IRandomSource random) => stateId == this.Expected.StateId() && random.NextFloat() < this.Probability;
}
