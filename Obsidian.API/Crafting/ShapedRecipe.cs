namespace Obsidian.API.Crafting;

/// <summary>
/// A crafting grid recipe with a fixed shape (<c>minecraft:crafting_shaped</c>): each character of
/// <see cref="Pattern"/> stands for the ingredient <see cref="Key"/> gives it.
/// </summary>
public sealed class ShapedRecipe : IRecipeWithResult, ICraftingRecipe
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required CraftingBookCategory Category { get; init; }

    /// <inheritdoc/>
    public required Ingredient Result { get; init; }

    /// <summary>
    /// Gets the shape, up to 3 rows of up to 3 characters each; a space is an empty slot (vanilla's <c>pattern</c>).
    /// </summary>
    public required IReadOnlyList<string> Pattern { get; init; }

    /// <summary>
    /// Gets the ingredient each character of <see cref="Pattern"/> stands for (vanilla's <c>key</c>).
    /// </summary>
    public required IReadOnlyDictionary<char, Ingredient> Key { get; init; }

    internal ShapedRecipe() { }
}
