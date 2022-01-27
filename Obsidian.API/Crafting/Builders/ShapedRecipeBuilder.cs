using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public class ShapedRecipeBuilder : IRecipeBuilder<ShapedRecipeBuilder>
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public ItemStack? Result { get; set; }

    public IReadOnlyList<string> Pattern { get; }

    public IReadOnlyDictionary<char, Ingredient> Key { get; }

    private readonly List<string> pattern = new();

    private readonly Dictionary<char, Ingredient> key = new();

    public ShapedRecipeBuilder()
    {
        Pattern = new ReadOnlyCollection<string>(pattern);

        Key = new ReadOnlyDictionary<char, Ingredient>(key);
    }

    public ShapedRecipeBuilder WithPattern(params string[] pattern)
    {
        if (pattern.Length > 3)
            throw new InvalidOperationException("Patterns must only have 3 total elements.");

        this.pattern.AddRange(pattern);

        return this;
    }

    public ShapedRecipeBuilder WithKey(char key, params ItemStack[] matches)
    {
        var ingredient = new Ingredient();

        foreach (var match in matches)
            ingredient.Add(match);

        this.key.Add(key, ingredient);

        return this;
    }

    public IRecipe Build()
    {
        if (pattern.Count <= 0)
            throw new InvalidOperationException("Patterns cannot be empty");

        if (key.Count <= 0)
            throw new InvalidOperationException("Keys cannot be empty.");

        return new ShapedRecipe
        (
            Name ?? throw new NullReferenceException("Recipe must have a name"),
            CraftingType.CraftingShaped,
            Group,
            Result != null ? new Ingredient { Result } : throw new NullReferenceException("Result is not set."),
            new ReadOnlyCollection<string>(new List<string>(pattern)),
            new ReadOnlyDictionary<char, Ingredient>(new Dictionary<char, Ingredient>(key))
        );
    }

    public ShapedRecipeBuilder WithName(string name)
    {
        Name = name;

        return this;
    }

    public ShapedRecipeBuilder SetResult(ItemStack result)
    {
        if (Result != null)
            throw new InvalidOperationException("Result is already set.");

        Result = result;

        return this;
    }

    public ShapedRecipeBuilder InGroup(string group)
    {
        Group = group;

        return this;
    }
}
