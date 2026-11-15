using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;

/// <summary>
/// The step of a recipe builder that sets the result.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IRecipeResult<TRecipe>
{
    /// <summary>
    /// Sets the stack the recipe crafts, with its count.
    /// </summary>
    public IRecipeBuilder<TRecipe> WithResult(ItemStack result);
}

