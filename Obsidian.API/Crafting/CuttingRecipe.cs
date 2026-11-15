namespace Obsidian.API.Crafting;

/// <summary>
/// A stonecutter recipe (<c>minecraft:stonecutting</c>): one <see cref="Ingredient"/> item becomes the stack in
/// <see cref="Result"/>.
/// </summary>
public sealed class CuttingRecipe : IRecipeWithResult
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required Ingredient Result { get; init; }

    /// <summary>
    /// Gets the items the stonecutter accepts as input, any one of which works (vanilla's <c>ingredient</c>).
    /// </summary>
    public required Ingredient Ingredient { get; init; }

    /// <summary>
    /// Gets the output count given to <see cref="Builders.CuttingRecipeBuilder"/>. The stonecutter crafts the stack in
    /// <see cref="Result"/>, with its own count, and doesn't read this.
    /// </summary>
    public required int Count { get; init; }

    internal CuttingRecipe() { }
}
