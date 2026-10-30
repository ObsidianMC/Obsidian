using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting;

/// <summary>
/// Dyes an item (<c>minecraft:crafting_dye</c>, 26.1): one <see cref="Target"/> and any number of
/// <see cref="Dye"/> items with a dye color craft <see cref="Result"/>, which keeps the target's components and mixes
/// the dyes into its <c>minecraft:dyed_color</c>, like vanilla's leather armour.
/// </summary>
public sealed class DyeRecipe : ICraftingRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required CraftingBookCategory Category { get; init; }

    /// <summary>The item dyed.</summary>
    public required Ingredient Target { get; init; }

    /// <summary>The dyes; only items with a <c>minecraft:dye</c> component match.</summary>
    public required Ingredient Dye { get; init; }

    /// <summary>
    /// The item and count the target becomes, and the components it sets and removes over the target's.
    /// </summary>
    public required ItemStack Result { get; init; }

    internal DyeRecipe() { }
}
