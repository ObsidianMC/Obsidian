namespace Obsidian.API.Crafting;

public sealed class SmithingRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required Ingredient Base { get; init; }

    public required Ingredient Addition { get; init; }

    internal SmithingRecipe() { }
}
