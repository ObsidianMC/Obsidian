using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.RuleTests;

[ConfiguredFeatureProperty("minecraft:always_true")]
public sealed class AlwaysTrueTest : IRuleTest
{
    public string Type { get; init; } = "minecraft:always_true";

    public bool Test(IBlock block, IRandomSource random) => true;
}
