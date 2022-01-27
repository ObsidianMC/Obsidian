using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public class ShapelessRecipeBuilder : IRecipeBuilder<ShapelessRecipeBuilder>
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public ItemStack? Result { get; set; }

    public IReadOnlyList<Ingredient> Ingredients { get; }

    private readonly List<Ingredient> ingredients = new();

    public ShapelessRecipeBuilder()
    {
        Ingredients = new ReadOnlyCollection<Ingredient>(ingredients);
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
        if (ingredients.Count <= 0)
            throw new InvalidOperationException("Ingredients must be filled with atleast 1 item.");

        return new ShapelessRecipe
        (
            Name ?? throw new NullReferenceException("Recipe must have a name"),
            CraftingType.CraftingShapeless,
            Group,
            Result != null ? new Ingredient { Result } : throw new NullReferenceException("Result is not set."),
            new ReadOnlyCollection<Ingredient>(new List<Ingredient>(ingredients))
        );
    }

    public ShapelessRecipeBuilder WithName(string name)
    {
        Name = name;

        return this;
    }

    public ShapelessRecipeBuilder SetResult(ItemStack result)
    {
        if (Result != null)
            throw new InvalidOperationException("Result is already set.");

        Result = result;

        return this;
    }

    public ShapelessRecipeBuilder InGroup(string group)
    {
        Group = group;

        return this;
    }
}
