using Obsidian.API.Inventory;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Turns an empty map into an explorer map pointing at the nearest structure of <see cref="Destination"/>.
/// </summary>
/// <remarks>
/// The map is left as is when the context has no <see cref="LootContext.ExplorationMaps"/> or origin, or when no structure is
/// found, like vanilla. The function draws no random numbers, so the other items of the table are unaffected.
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

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (stack.Type != Material.Map || context.Origin is null || context.ExplorationMaps is null)
            return stack;

        return context.ExplorationMaps.Create(new ExplorationMapRequest(this.Destination, context.Origin.Value, this.SearchRadius,
            this.SkipExistingChunks, this.Zoom, this.Decoration)) ?? stack;
    }
}
