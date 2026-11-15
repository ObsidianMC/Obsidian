namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The step of a recipe builder that sets the identifier.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface INamedRecipe<TRecipe>
{
    /// <summary>
    /// Sets the recipe's identifier, like <c>myplugin:ruby_block</c>.
    /// </summary>
    public IRecipeResult<TRecipe> WithIdentifier(string name);
}
