namespace Obsidian.WorldData.Features;

/// <summary>
/// Buried fossils built from structure templates, like vanilla's FossilFeature.
/// </summary>
/// <remarks>
/// Not placeable yet: vanilla loads <c>data/minecraft/structure/fossil/*.nbt</c> templates and places them with
/// <c>StructureTemplate.placeInWorld</c> and the <c>fossil_rot</c>/<c>fossil_coal</c>/<c>fossil_diamonds</c> processor lists
/// (block_rot integrity rolls per template block). Obsidian has neither the templates nor a template placer, so this draws
/// the same leading random values as vanilla (rotation, then template index) and places nothing.
/// </remarks>
[ConfiguredFeatureClass("minecraft:fossil")]
public sealed class FossilFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:fossil";

    /// <summary>
    /// Template ids of the bone structures (e.g. <c>minecraft:fossil/spine_1</c>).
    /// </summary>
    public required string[] FossilStructures { get; init; }

    /// <summary>
    /// Template ids of the ore overlays, paired by index with <see cref="FossilStructures"/>.
    /// </summary>
    public required string[] OverlayStructures { get; init; }

    /// <summary>
    /// Processor list id applied to the fossil (e.g. <c>minecraft:fossil_rot</c>).
    /// </summary>
    public required string FossilProcessors { get; init; }

    /// <summary>
    /// Processor list id applied to the overlay (e.g. <c>minecraft:fossil_coal</c>).
    /// </summary>
    public required string OverlayProcessors { get; init; }

    /// <summary>
    /// The fossil is skipped when more of its bounding box corners are air or fluid.
    /// </summary>
    public required int MaxEmptyCornersAllowed { get; init; }

    public override bool Place(FeatureContext context)
    {
        if (!context.Level.EnsureCanWrite(context.Origin))
            return false;

        var random = context.Random;

        // Rotation.getRandom, then the template index.
        random.NextInt(4);
        random.NextInt(this.FossilStructures.Length);
        return false;
    }
}
