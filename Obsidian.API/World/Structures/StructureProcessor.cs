using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Structures;

/// <summary>
/// Changes or drops template blocks as they're placed, like vanilla's <c>StructureProcessor</c>. Data names a
/// processor by its <c>processor_type</c>; plugins add types with
/// <c>RegisterWorldgenType&lt;StructureProcessor, T&gt;</c>.
/// </summary>
/// <remarks>Instances come from processor-list data and are shared between threads; keep them stateless.</remarks>
public abstract class StructureProcessor
{
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Whether the processor's result for a block depends on the template's other blocks (it uses
    /// <see cref="FinalizeProcessing"/> or a random shared between blocks), so the whole template must be processed
    /// even when only part of it is placed.
    /// </summary>
    public virtual bool ProcessesWholeTemplate => false;

    /// <summary>
    /// Returns the block to place for <paramref name="current"/>, or <c>null</c> to skip it.
    /// </summary>
    /// <param name="level">The level the template is placed in.</param>
    /// <param name="origin">World position of the template's origin.</param>
    /// <param name="pivot">World position of the rotation pivot.</param>
    /// <param name="original">The raw template block (template-relative position).</param>
    /// <param name="current">The block so far, at its world position.</param>
    /// <param name="placement">How the template is placed.</param>
    public virtual StructureBlockInfo? ProcessBlock(
        IWorldGenLevel level,
        Vector origin,
        Vector pivot,
        StructureBlockInfo original,
        StructureBlockInfo current,
        TemplatePlacement placement) => current;

    /// <summary>
    /// Vanilla <c>finalizeProcessing</c>: runs once all blocks went through <see cref="ProcessBlock"/>, with the kept
    /// blocks (<paramref name="processed"/>) and their template originals at the same indices.
    /// </summary>
    /// <returns>The blocks to place.</returns>
    public virtual List<StructureBlockInfo> FinalizeProcessing(
        IWorldGenLevel level,
        Vector origin,
        Vector pivot,
        IReadOnlyList<StructureBlockInfo> originals,
        List<StructureBlockInfo> processed,
        TemplatePlacement placement) => processed;
}

/// <summary>
/// What a <see cref="StructureProcessor"/> sees of how its template is placed, like the parts of vanilla's
/// <c>StructurePlaceSettings</c> processors use. Only the server creates placements.
/// </summary>
public abstract class TemplatePlacement
{
    internal TemplatePlacement()
    {
    }

    /// <summary>
    /// Vanilla <c>StructurePlaceSettings.getRandom</c>: the placement's random, or, when it has none, a new random
    /// seeded from <paramref name="position"/>.
    /// </summary>
    public abstract IRandomSource GetRandom(Vector position);

    /// <summary>
    /// <see cref="GetRandom"/> for a moment, without allocating a positional random; give it back with
    /// <see cref="ReturnRandom"/>.
    /// </summary>
    internal abstract IRandomSource RentRandom(Vector position);

    internal abstract void ReturnRandom(IRandomSource random);
}
