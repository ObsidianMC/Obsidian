using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

/// <summary>
/// Matches blocks in a block tag.
/// </summary>
[ConfiguredFeatureProperty("minecraft:tag_match")]
public sealed class TagMatchTest : IRuleTest
{
    public string Type { get; init; } = "minecraft:tag_match";

    public required string Tag { get; init; }

    private BlockSet Blocks => field ??= new BlockSet($"#{this.Tag}");

    public bool Test(IBlock block, IRandomSource random) => this.Blocks.Contains(block);
}
