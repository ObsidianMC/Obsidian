namespace Obsidian.API.Crafting;

/// <summary>
/// Recipe for smithing netherite gear
/// </summary>
public sealed class SmithingTransformRecipe : IRecipeWithResult
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }

    public required Ingredient Template { get; init; }

    internal SmithingTransformRecipe() { }
}

/// <summary>
/// Used for applying armor trims
/// </summary>
public sealed class SmithingTrimRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }

    public required Ingredient Template { get; init; }

    //The fact that this has no result makes me 🤪🤪🤪
    //So I make it public :)
    public SmithingTrimRecipe() { }
}
