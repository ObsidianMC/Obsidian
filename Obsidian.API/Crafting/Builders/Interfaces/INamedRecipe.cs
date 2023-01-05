namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface INamedRecipe<TRecipe>
{
    public IRecipeResult<TRecipe> WithName(string name);
}
