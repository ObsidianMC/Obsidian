using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;

public interface IRecipeResult<TRecipe>
{
    public IRecipeBuilder<TRecipe> WithResult(ItemStack result);
}

