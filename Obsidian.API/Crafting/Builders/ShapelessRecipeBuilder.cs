using Obsidian.API.Crafting.Builders.Interfaces;
using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public sealed class ShapelessRecipeBuilder : BaseRecipeBuilder<ShapelessRecipe>, IIngredientRecipe<IGroupedRecipe<ShapelessRecipe>>
{
    private readonly CraftingBookCategory category;

    private readonly List<Ingredient> ingredients = new();

    private ShapelessRecipeBuilder(CraftingBookCategory category) => this.category = category;

    public static IIngredientRecipe<IGroupedRecipe<ShapelessRecipe>> Create(CraftingBookCategory category) => new ShapelessRecipeBuilder(category);

    public IGroupedRecipe<ShapelessRecipe> AddIngredient(params ItemStack[] items)
    {
        var ingredient = new Ingredient();

        foreach (var item in items)
            ingredient.Add(item);

        this.ingredients.Add(ingredient);

        return this;
    }

    public override ShapelessRecipe Build()
    {
        if (this.ingredients.Count <= 0)
            throw new InvalidOperationException("Ingredients must be filled with atleast 1 item.");

        return new ShapelessRecipe
        {
            Identifier = this.Identifier ?? throw new NullReferenceException("Recipe must have a name"),
            Type = CraftingType.CraftingShapeless,
            Group = this.Group,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
            Ingredients = new ReadOnlyCollection<Ingredient>(new List<Ingredient>(this.ingredients)),
            Category = this.category
        };
    }
}
