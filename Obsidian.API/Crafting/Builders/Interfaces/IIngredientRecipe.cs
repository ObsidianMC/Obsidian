namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IIngredientRecipe<TBuilder>
{
    public TBuilder AddIngredient(params ItemStack[] items);
}
