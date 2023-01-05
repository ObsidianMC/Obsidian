namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IBaseIngredientRecipe<TBuilder>
{
    public IUpgradeIngredientRecipe<TBuilder> AddBaseIngredient(params ItemStack[] items);
}

public interface IUpgradeIngredientRecipe<TBuilder>
{
    public TBuilder AddUpgradeIngredient(params ItemStack[] items);
}
