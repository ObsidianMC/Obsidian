using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Turns an empty map into an explorer map pointing at the nearest structure of <see cref="Destination"/>.
/// </summary>
/// <remarks>
/// Obsidian can't search for structures or create map data yet, so the map is always left as is. That's also what
/// vanilla does when no structure is found. The function draws no random numbers either way, so the other items of the
/// table are unaffected.
/// </remarks>
[LootType("minecraft:exploration_map")]
public sealed class ExplorationMapFunction : LootFunction
{
    /// <summary>
    /// The structure tag to search for.
    /// </summary>
    public string Destination { get; init; } = "minecraft:on_treasure_maps";

    /// <summary>
    /// The map decoration type marking the structure.
    /// </summary>
    public string Decoration { get; init; } = "minecraft:mansion";

    public int Zoom { get; init; } = 2;

    /// <summary>
    /// Search radius in chunks.
    /// </summary>
    public int SearchRadius { get; init; } = 50;

    /// <summary>
    /// Skips structures that were already generated.
    /// </summary>
    public bool SkipExistingChunks { get; init; } = true;

    protected override ItemStack Run(ItemStack stack, LootContext context) => stack;
}
