namespace Obsidian.WorldData.BlockPredicates;

[ConfiguredFeatureProperty("minecraft:true")]
public sealed class TruePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:true";

    public bool Test(IWorldGenLevel level, Vector position) => true;
}
