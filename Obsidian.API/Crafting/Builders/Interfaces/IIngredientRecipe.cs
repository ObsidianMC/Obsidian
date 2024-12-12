using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IIngredientRecipe<TBuilder>
{
    public TBuilder WithIngredient(params ItemStack[] items);
}

public interface IShapelessIngredientRecipe<TRecipe>
{
    public IGroupedRecipe<TRecipe> AddIngredients(params Ingredient[] ingredients);
}
