using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Decides whether a block may be replaced, used by ore targets. Like vanilla's RuleTest.
/// </summary>
public interface IRuleTest
{
    /// <summary>
    /// Gets the registered codec type identifier for this world generation component.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Tests whether a block is an eligible replacement target, using randomness when required.
    /// </summary>
    public bool Test(IBlock block, IRandomSource random);
}
