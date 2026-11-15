using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The first steps of a <see cref="ShapedRecipeBuilder"/>: its keys, then its pattern.
/// </summary>
public interface IPatternedRecipe
{ 
    /// <summary>
    /// Makes the character <paramref name="key"/> stand for any of <paramref name="items"/> in the pattern.
    /// </summary>
    public IPatternedRecipe WithKey(char key, params ItemStack[] items);

    /// <summary>
    /// Sets the recipe's shape: up to 3 rows, each character a key or a space for an empty slot.
    /// </summary>
    public IGroupedRecipe<ShapedRecipe> WithPattern(params string[] pattern);
}
