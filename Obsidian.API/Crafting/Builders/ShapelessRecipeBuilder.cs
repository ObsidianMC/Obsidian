using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public class ShapelessRecipeBuilder : IRecipeBuilder<ShapelessRecipeBuilder>
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public ItemStack? Result { get; set; }

    public CraftingBookCategory Category { get; set; }

    public IReadOnlyList<Ingredient> Ingredients { get; }

    private readonly List<Ingredient> ingredients = new();

    public ShapelessRecipeBuilder()
    {
        this.Ingredients = new ReadOnlyCollection<Ingredient>(this.ingredients);
    }

    public ShapelessRecipeBuilder AddIngredients(params ItemStack[] items)
    {
        var ingredient = new Ingredient();

        foreach (var item in items)
            ingredient.Add(item);

        return this;
    }

    public IRecipe Build()
    {
        if (this.ingredients.Count <= 0)
            throw new InvalidOperationException("Ingredients must be filled with atleast 1 item.");

        return new ShapelessRecipe
        {
            Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
            Type = CraftingType.CraftingShapeless,
            Group = this.Group,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
            Ingredients = new ReadOnlyCollection<Ingredient>(new List<Ingredient>(this.ingredients)),
            Category = this.Category
        };
    }

    public ShapelessRecipeBuilder InCategory(CraftingBookCategory category)
    {
        this.Category = category;

        return this;
    }

    public ShapelessRecipeBuilder WithName(string name)
    {
        this.Name = name;

        return this;
    }

    public ShapelessRecipeBuilder SetResult(ItemStack result)
    {
        if (this.Result != null)
            throw new InvalidOperationException("Result is already set.");

        this.Result = result;

        return this;
    }

    public ShapelessRecipeBuilder InGroup(string group)
    {
        this.Group = group;

        return this;
    }
}
