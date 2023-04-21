namespace Obsidian.API.Crafting;

public sealed class SmithingRecipe : IRecipeWithResult
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }

    internal SmithingRecipe() { }
}

/// <summary>
/// Recipe for smithing netherite gear
/// </summary>
/// 
/// <remarks>
/// Only used if 1.20 feature flag was enabled.
/// </remarks>
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
/// 
/// <remarks>
/// Only used if 1.20 feature flag was enabled.
/// </remarks>
public sealed class SmithingTrimRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }

    public required Ingredient Template { get; init; }

    internal SmithingTrimRecipe() { }
}
