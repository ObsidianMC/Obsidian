using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;
using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public sealed class ShapedRecipeBuilder : BaseRecipeBuilder<ShapedRecipe>, IPatternedRecipe
{
    private readonly CraftingBookCategory category;

    private readonly List<string> pattern = [];

    private readonly Dictionary<char, Ingredient> key = [];

    private ShapedRecipeBuilder(CraftingBookCategory category) => this.category = category;

    public static IPatternedRecipe Create(CraftingBookCategory category) => new ShapedRecipeBuilder(category);

    public IPatternedRecipe WithKey(char key, params ItemStack[] matches)
    {
        var ingredient = new Ingredient();

        foreach (var match in matches)
            ingredient.Add(match);

        this.key.Add(key, ingredient);

        return this;
    }

    public IGroupedRecipe<ShapedRecipe> WithPattern(params string[] pattern)
    {
        if (pattern.Length > 3)
            throw new InvalidOperationException("Patterns must only have 3 total elements.");
        if (this.key.Count == 0)
            throw new InvalidOperationException("Shaped recipes must have keys set before pattern.");

        this.pattern.AddRange(pattern);

        return this;
    }

    public override ShapedRecipe Build()
    {
        if (this.pattern.Count <= 0)
            throw new InvalidOperationException("Patterns cannot be empty");

        if (this.key.Count <= 0)
            throw new InvalidOperationException("Keys cannot be empty.");

        return new ShapedRecipe
        {
            Identifier = this.Identifier ?? throw new NullReferenceException("Recipe must have a name"),
            Type = CraftingType.CraftingShaped,
            Group = this.Group,
            Result = this.Result != null ? [this.Result] : throw new NullReferenceException("Result is not set."),
            Pattern = new ReadOnlyCollection<string>(new List<string>(this.pattern)),
            Key = new ReadOnlyDictionary<char, Ingredient>(new Dictionary<char, Ingredient>(this.key)),
            Category = this.category,
        };
    }

}
