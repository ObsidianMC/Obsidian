namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Leaves template blocks unchanged, like vanilla's <c>NopProcessor</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:nop")]
public sealed class NopProcessor : StructureProcessor
{
}
