namespace Obsidian.API.Crafting;

public sealed class ShapedRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    public required CraftingBookCategory Category { get; init; }

    public required Ingredient Result { get; init; }

    public required IReadOnlyList<string> Pattern { get; init; }

    public required IReadOnlyDictionary<char, Ingredient> Key { get; init; }

    internal ShapedRecipe() { }
}
