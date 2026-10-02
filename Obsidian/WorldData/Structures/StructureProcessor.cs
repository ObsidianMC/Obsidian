namespace Obsidian.WorldData.Structures;

/// <summary>
/// Changes or drops template blocks as they're placed, like vanilla's <c>StructureProcessor</c>.
/// </summary>
/// <remarks>Instances come from processor-list data and are shared between threads; keep them stateless.</remarks>
public abstract class StructureProcessor
{
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Whether the processor's result for a block depends on the template's other blocks (it uses
    /// <see cref="FinalizeProcessing"/> or a random shared between blocks), so the whole template must be processed even
    /// when only part of it is placed.
    /// </summary>
    public virtual bool ProcessesWholeTemplate => false;

    /// <summary>
    /// Returns the block to place for <paramref name="current"/>, or <c>null</c> to skip it.
    /// </summary>
    /// <param name="origin">World position of the template's origin.</param>
    /// <param name="pivot">World position of the rotation pivot.</param>
    /// <param name="original">The raw template block (template-relative position).</param>
    /// <param name="current">The block so far, at its world position.</param>
    public virtual StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings) => current;

    /// <summary>
    /// Vanilla <c>finalizeProcessing</c>: runs once all blocks went through <see cref="ProcessBlock"/>, with the kept blocks
    /// (<paramref name="processed"/>) and their template originals at the same indices.
    /// </summary>
    /// <returns>The blocks to place.</returns>
    public virtual List<StructureBlockInfo> FinalizeProcessing(IWorldGenLevel level, Vector origin, Vector pivot,
        IReadOnlyList<StructureBlockInfo> originals, List<StructureBlockInfo> processed, StructurePlaceSettings settings) => processed;
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
