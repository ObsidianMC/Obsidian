namespace Obsidian.API.Crafting;

public sealed class ShapelessRecipe : IRecipe
{
    public string Name { get; set; }

    public required CraftingType Type { get; init; }

    public required CraftingBookCategory Category { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required IReadOnlyList<Ingredient> Ingredients { get; init; }
}
