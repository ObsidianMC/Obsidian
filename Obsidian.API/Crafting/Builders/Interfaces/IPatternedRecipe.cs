namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IRecipePattern
{
    public IRecipePattern WithKey(char key, params ItemStack[] items);

    public IRecipeGroup<ShapedRecipe> WithPattern(params string[] pattern);
}
