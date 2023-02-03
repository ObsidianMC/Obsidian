namespace Obsidian.API.Crafting;

public sealed class CuttingRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required Ingredient Result { get; init; }

    public required Ingredient Ingredient { get; init; }

    public required int Count { get; init; }

    internal CuttingRecipe() { }
}
