using Obsidian.WorldData.Structures.Placement;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Structures sharing a placement (one of them is picked per start chunk), like vanilla's <c>StructureSet</c>. The sets
/// vanilla defines are in the generated <c>StructureSets</c> registry.
/// </summary>
public sealed class StructureSet
{
    /// <summary>
    /// The registry id, e.g. <c>minecraft:villages</c>.
    /// </summary>
    public string Identifier { get; init; } = string.Empty;

    public required ImmutableArray<StructureSetEntry> Structures { get; init; }

    public required StructurePlacement Placement { get; init; }
}

/// <summary>
/// A structure of a <see cref="StructureSet"/> and its weight in the pick.
/// </summary>
public sealed class StructureSetEntry
{
    public required Structure Structure { get; init; }

    public required int Weight { get; init; }
}
