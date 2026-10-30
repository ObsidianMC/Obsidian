using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting;

/// <summary>
/// Imbues items with a potion (<c>minecraft:crafting_imbue</c>, 26.1): a full 3x3 grid of <see cref="Material"/>
/// around a <see cref="Source"/> crafts <see cref="Result"/>, holding the source's <c>minecraft:potion_contents</c>,
/// like vanilla's tipped arrows.
/// </summary>
public sealed class ImbueRecipe : ICraftingRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required CraftingBookCategory Category { get; init; }

    /// <summary>The item in the middle of the grid, whose potion the result takes.</summary>
    public required Ingredient Source { get; init; }

    /// <summary>The items around it.</summary>
    public required Ingredient Material { get; init; }

    /// <summary>The item and count crafted.</summary>
    public required ItemStack Result { get; init; }

    internal ImbueRecipe() { }
}
