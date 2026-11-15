using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting;

/// <summary>
/// Imbues items with a potion (<c>minecraft:crafting_imbue</c>, 26.1): a full 3x3 grid of <see cref="Material"/>
/// around a <see cref="Source"/> crafts <see cref="Result"/>, holding the source's <c>minecraft:potion_contents</c>,
/// like vanilla's tipped arrows.
/// </summary>
public sealed class ImbueRecipe : ICraftingRecipe
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required CraftingBookCategory Category { get; init; }

    /// <summary>The item in the middle of the grid, whose potion the result takes.</summary>
    public required Ingredient Source { get; init; }

    /// <summary>The items around it.</summary>
    public required Ingredient Material { get; init; }

    /// <summary>The item, count and components crafted.</summary>
    public required ItemStack Result { get; init; }

    internal ImbueRecipe() { }
}
