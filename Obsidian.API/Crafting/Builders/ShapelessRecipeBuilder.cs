using Obsidian.API.Crafting.Builders.Interfaces;
using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

public sealed class ShapelessRecipeBuilder : BaseRecipeBuilder<ShapelessRecipe>, IShapelessIngredientRecipe<ShapelessRecipe>
{
    private readonly CraftingBookCategory category;

    private readonly List<Ingredient> ingredients = [];

    private ShapelessRecipeBuilder(CraftingBookCategory category) => this.category = category;

    public static IShapelessIngredientRecipe<ShapelessRecipe> Create(CraftingBookCategory category) => new ShapelessRecipeBuilder(category);

    public IGroupedRecipe<ShapelessRecipe> AddIngredients(params Ingredient[] ingredients)
    {
        foreach (var ingredient in ingredients)
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
            Result = this.Result != null ? [this.Result] : throw new NullReferenceException("Result is not set."),
            Ingredients = new ReadOnlyCollection<Ingredient>(new List<Ingredient>(this.ingredients)),
            Category = this.category
        };
    }
}
