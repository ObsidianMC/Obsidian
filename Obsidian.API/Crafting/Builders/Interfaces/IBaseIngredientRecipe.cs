namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IBaseIngredientRecipe<TRecipe>
{
    public IUpgradeIngredientRecipe<TRecipe> WithBaseIngredient(params ItemStack[] items);
}

public interface IUpgradeIngredientRecipe<TRecipe>
{
    public INamedRecipe<TRecipe> WithUpgradeIngredient(params ItemStack[] items);
}
