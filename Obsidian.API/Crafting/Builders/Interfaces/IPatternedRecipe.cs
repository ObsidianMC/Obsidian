using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IPatternedRecipe
{ 
    public IPatternedRecipe WithKey(char key, params ItemStack[] items);

    public IGroupedRecipe<ShapedRecipe> WithPattern(params string[] pattern);
}
