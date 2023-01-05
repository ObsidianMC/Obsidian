namespace Obsidian.API.Crafting.Builders.Interfaces;

public interface IRecipeResult<TRecipe>
{
    public IRecipeBuilder<TRecipe> SetResult(ItemStack result);
}
