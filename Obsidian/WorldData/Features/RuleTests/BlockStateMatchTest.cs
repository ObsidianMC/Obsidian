using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// Matches one exact block state.
/// </summary>
[ConfiguredFeatureProperty("minecraft:blockstate_match")]
public sealed class BlockStateMatchTest : IStateRuleTest
{
    public string Type { get; init; } = "minecraft:blockstate_match";

    public required SimpleBlockState BlockState { get; init; }

    private IBlock Expected => field ??= BlocksRegistry.GetFromSimpleState(this.BlockState);

    public bool Test(IBlock block, IRandomSource random) => block.IsSameState(this.Expected);

    public bool Test(int stateId, IRandomSource random) => stateId == this.Expected.StateId();
}
