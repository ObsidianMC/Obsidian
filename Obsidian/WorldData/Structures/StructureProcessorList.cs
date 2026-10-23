using Obsidian.API.World.Structures;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A named list of processors, like vanilla's <c>StructureProcessorList</c>; generated from
/// <c>Assets/worldgen/processor_lists</c> into <c>ProcessorLists</c>.
/// </summary>
public sealed class StructureProcessorList
{
    /// <summary>Registry id (e.g. <c>minecraft:fossil_rot</c>), or empty when defined inline.</summary>
    public string Identifier { get; init; } = string.Empty;

    public ImmutableArray<StructureProcessor> Processors { get; init; } = [];
}
