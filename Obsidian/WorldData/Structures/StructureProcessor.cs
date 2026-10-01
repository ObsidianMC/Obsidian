namespace Obsidian.WorldData.Structures;

/// <summary>
/// Changes or drops template blocks as they're placed, like vanilla's <c>StructureProcessor</c>.
/// </summary>
/// <remarks>Instances come from processor-list data and are shared between threads; keep them stateless.</remarks>
public abstract class StructureProcessor
{
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Returns the block to place for <paramref name="current"/>, or <c>null</c> to skip it.
    /// </summary>
    /// <param name="origin">World position of the template's origin.</param>
    /// <param name="pivot">World position of the rotation pivot.</param>
    /// <param name="original">The raw template block (template-relative position).</param>
    /// <param name="current">The block so far, at its world position.</param>
    public abstract StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings);
}

/// <summary>
/// A named list of processors, like vanilla's <c>StructureProcessorList</c>; generated from
/// <c>Assets/worldgen/processor_lists</c> into <c>ProcessorLists</c>.
/// </summary>
public sealed class StructureProcessorList
{
    /// <summary>Registry id (e.g. <c>minecraft:fossil_rot</c>), or empty when defined inline.</summary>
    public string Identifier { get; init; } = string.Empty;

    public StructureProcessor[] Processors { get; init; } = [];
}
