using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Crafting;

/// <summary>
/// Turns an item into another, keeping its components (<c>minecraft:crafting_transmute</c>): one
/// <see cref="Input"/> and <see cref="MinMaterialCount"/> to <see cref="MaxMaterialCount"/> <see cref="Material"/>
/// items, anywhere in the grid, craft <see cref="ResultCount"/> of <see cref="ResultItem"/>, with
/// <see cref="ResultComponents"/> set and <see cref="RemovedResultComponents"/> removed over the input's components.
/// Vanilla dyes shulker boxes and bundles, and clones maps, with it.
/// </summary>
public sealed class TransmuteRecipe : ICraftingRecipe
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required CraftingBookCategory Category { get; init; }

    /// <summary>The item transmuted, whose components the result keeps.</summary>
    public required Ingredient Input { get; init; }

    /// <summary>The items it's transmuted with.</summary>
    public required Ingredient Material { get; init; }

    /// <summary>The fewest material items the recipe takes, from 1.</summary>
    public required int MinMaterialCount { get; init; }

    /// <summary>The most material items the recipe takes, up to 8.</summary>
    public required int MaxMaterialCount { get; init; }

    /// <summary>The item crafted, or <c>null</c> for the input's own item (map cloning).</summary>
    public Item? ResultItem { get; init; }

    /// <summary>How many are crafted, before <see cref="AddMaterialCountToResult"/> adds to it.</summary>
    public required int ResultCount { get; init; }

    /// <summary>Whether each material item adds one to the result, as each blank map makes a copy.</summary>
    public bool AddMaterialCountToResult { get; init; }

    /// <summary>The components the result sets, replacing the input's of the same types.</summary>
    public IReadOnlyList<DataComponent> ResultComponents { get; init; } = [];

    /// <summary>The components the result removes, the input's or its item's.</summary>
    public IReadOnlyList<DataComponentType> RemovedResultComponents { get; init; } = [];

    internal TransmuteRecipe() { }
}
