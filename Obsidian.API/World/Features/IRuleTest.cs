using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Decides whether a block may be replaced, used by ore targets. Like vanilla's RuleTest.
/// </summary>
public interface IRuleTest
{
    public string Type { get; }

    public bool Test(IBlock block, IRandomSource random);
}
