namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The step of a recipe builder that sets the recipe book group.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IGroupedRecipe<TRecipe>
{
    /// <summary>
    /// Puts the recipe in a recipe book group, shown as one entry with the group's other recipes.
    /// </summary>
    public INamedRecipe<TRecipe> InGroup(string group);

    /// <summary>
    /// Skips the group step. The recipe is in no recipe book group unless one was set earlier.
    /// </summary>
    public INamedRecipe<TRecipe> HasNoGroup();
}
