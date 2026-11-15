using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The step of a recipe builder that takes the ingredient.
/// </summary>
/// <typeparam name="TBuilder">The next step.</typeparam>
public interface IIngredientRecipe<TBuilder>
{
    /// <summary>
    /// Adds <paramref name="items"/> to the stacks the ingredient accepts, any one of which works.
    /// </summary>
    public TBuilder WithIngredient(params ItemStack[] items);
}

/// <summary>
/// The step of a shapeless recipe builder that takes the ingredients.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IShapelessIngredientRecipe<TRecipe>
{
    /// <summary>
    /// Adds <paramref name="ingredients"/>, each one item of the recipe.
    /// </summary>
    public IGroupedRecipe<TRecipe> AddIngredients(params Ingredient[] ingredients);
}
