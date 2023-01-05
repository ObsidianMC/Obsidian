namespace Obsidian.API.Crafting.Builders.Interfaces;

public interface ISetResult<TRecipe>
{
    public IRecipeBuilder<TRecipe> SetResult(ItemStack result);
}
