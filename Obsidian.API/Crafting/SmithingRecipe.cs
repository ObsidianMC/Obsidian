namespace Obsidian.API.Crafting;

/// <summary>
/// Recipe for smithing netherite gear
/// </summary>
public sealed class SmithingTransformRecipe : IRecipeWithResult
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
    /// Gets the item upgraded, whose components the result keeps (vanilla's <c>base</c>).
    /// </summary>
    public required Ingredient Base { get; init; }

    /// <summary>
    /// Gets the material added, like a netherite ingot (vanilla's <c>addition</c>).
    /// </summary>
    public required Ingredient Addition { get; init; }

    /// <summary>
    /// Gets the smithing template the recipe needs (vanilla's <c>template</c>).
    /// </summary>
    public required Ingredient Template { get; init; }

    internal SmithingTransformRecipe() { }
}

/// <summary>
/// Used for applying armor trims
/// </summary>
public sealed class SmithingTrimRecipe : IRecipe
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <summary>
    /// Gets the armour pieces the trim can go on (vanilla's <c>base</c>).
    /// </summary>
    public required Ingredient Base { get; init; }

    /// <summary>
    /// Gets the material deciding the trim's color (vanilla's <c>addition</c>).
    /// </summary>
    public required Ingredient Addition { get; init; }

    /// <summary>
    /// Gets the trim templates the recipe accepts (vanilla's <c>template</c>).
    /// </summary>
    public required Ingredient Template { get; init; }

    /// <summary>
    /// The trim pattern the template applies, like <c>minecraft:bolt</c>.
    /// </summary>
    public string? Pattern { get; init; }

    //The fact that this has no result makes me 🤪🤪🤪
    //So I make it public :)
    /// <summary>
    /// Creates a trim recipe; set its required properties with an object initializer.
    /// </summary>
    public SmithingTrimRecipe() { }
}
