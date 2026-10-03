using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// A rule test that can test a block by its state id, for features that read state ids.
/// </summary>
internal interface IStateRuleTest : IRuleTest
{
    /// <summary>
    /// <see cref="IRuleTest.Test"/> for the block with state id <paramref name="stateId"/>.
    /// </summary>
    public bool Test(int stateId, IRandomSource random);
}
