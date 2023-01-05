namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IGroupRecipeBuilder<TRecipe>
{
    public INamedRecipeBuilder<TRecipe> InGroup(string group);

    public INamedRecipeBuilder<TRecipe> HasNoGroup();
}
