namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The step of a stonecutter recipe builder that sets the output count.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IOutputCountRecipe<TRecipe>
{
    /// <summary>
    /// Sets the recipe's <see cref="CuttingRecipe.Count"/>.
    /// </summary>
    public IGroupedRecipe<TRecipe> WithOutputCount(int count);
}
