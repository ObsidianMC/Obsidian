using Obsidian.API.Inventory;

namespace Obsidian.API.Loot;

/// <summary>
/// Creates explorer maps for loot, like vanilla's <c>ExplorationMapFunction</c> does with its level.
/// </summary>
public interface IExplorationMapFactory
{
    /// <summary>
    /// A filled map centered on the nearest structure of the request's destination tag, with its biomes previewed and the
    /// structure marked, or <c>null</c> when no structure is found within the search radius.
    /// </summary>
    public ItemStack? Create(ExplorationMapRequest request);
}

/// <summary>
/// The parameters of a <c>minecraft:exploration_map</c> loot function.
/// </summary>
/// <param name="Destination">The structure tag to search for, e.g. <c>minecraft:on_treasure_maps</c>.</param>
/// <param name="Origin">Where the search starts: the loot's origin.</param>
/// <param name="SearchRadius">How many rings of placement regions (or chunks) to search.</param>
/// <param name="SkipKnownStructures">Skips structures another map already points to.</param>
/// <param name="Zoom">The map scale, 0 to 4.</param>
/// <param name="Decoration">The map decoration type marking the structure, e.g. <c>minecraft:red_x</c>.</param>
public sealed record ExplorationMapRequest(string Destination, VectorF Origin, int SearchRadius, bool SkipKnownStructures, int Zoom,
    string Decoration);
