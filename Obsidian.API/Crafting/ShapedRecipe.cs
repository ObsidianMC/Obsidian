namespace Obsidian.API.Crafting;

public sealed class ShapedRecipe : IRecipe
{
    public string Name { get; set; }

    public CraftingType Type { get; set; }

    public string? Group { get; set; }

    public Ingredient Result { get; set; }

    public IReadOnlyList<string> Pattern { get; set; }

    public IReadOnlyDictionary<char, Ingredient> Key { get; set; }

    public ShapedRecipe(string name, CraftingType type, string? group, Ingredient result, IReadOnlyList<string> pattern, IReadOnlyDictionary<char, Ingredient> key)
    {
        Name = name;
        Type = type;
        Group = group;
        Result = result;
        Pattern = pattern;
        Key = key;
    }
}
