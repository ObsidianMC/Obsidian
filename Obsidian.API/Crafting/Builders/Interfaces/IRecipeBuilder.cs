namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The last step of a recipe builder.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IRecipeBuilder<TRecipe>
{
    /// <summary>
    /// Creates the recipe from the steps given.
    /// </summary>
    public TRecipe Build();
}
