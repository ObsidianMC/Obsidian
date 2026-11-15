using Obsidian.API.Crafting.Builders.Interfaces;
using System.Collections.ObjectModel;

namespace Obsidian.API.Crafting.Builders;

/// <summary>
/// Builds a <see cref="ShapelessRecipe"/>: start with <see cref="Create"/>, then give the ingredients, group,
/// identifier and result in that order.
/// </summary>
public sealed class ShapelessRecipeBuilder : BaseRecipeBuilder<ShapelessRecipe>, IShapelessIngredientRecipe<ShapelessRecipe>
{
    private readonly CraftingBookCategory category;

    private readonly List<Ingredient> ingredients = [];

    private ShapelessRecipeBuilder(CraftingBookCategory category) => this.category = category;

    /// <summary>
    /// Starts a shapeless recipe for the recipe book tab <paramref name="category"/>.
    /// </summary>
    public static IShapelessIngredientRecipe<ShapelessRecipe> Create(CraftingBookCategory category) => new ShapelessRecipeBuilder(category);

    /// <inheritdoc/>
    public IGroupedRecipe<ShapelessRecipe> AddIngredients(params Ingredient[] ingredients)
    {
        foreach (var ingredient in ingredients)
            this.ingredients.Add(ingredient);

        return this;
    }

    /// <inheritdoc/>
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
