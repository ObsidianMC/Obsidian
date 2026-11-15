namespace Obsidian.API.Crafting;

/// <summary>
/// A crafting grid recipe whose ingredients can go in any slot (<c>minecraft:crafting_shapeless</c>).
/// </summary>
public sealed class ShapelessRecipe : IRecipeWithResult, ICraftingRecipe
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public required CraftingBookCategory Category { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required Ingredient Result { get; init; }

    /// <summary>
    /// Gets the ingredients, one item per entry, in any order (vanilla's <c>ingredients</c>).
    /// </summary>
    public required IReadOnlyList<Ingredient> Ingredients { get; init; }

    internal ShapelessRecipe() { }
}
