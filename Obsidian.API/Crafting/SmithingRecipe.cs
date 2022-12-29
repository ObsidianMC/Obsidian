namespace Obsidian.API.Crafting;

public sealed class SmithingRecipe : IRecipe
{
    public string Name { get; set; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }
}
