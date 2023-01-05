namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IBaseIngredientRecipe<TBuilder>
{
    public IUpgradeIngredientRecipe<TBuilder> WithBaseIngredient(params ItemStack[] items);
}

public interface IUpgradeIngredientRecipe<TBuilder>
{
    public TBuilder WithUpgradeIngredient(params ItemStack[] items);
}
