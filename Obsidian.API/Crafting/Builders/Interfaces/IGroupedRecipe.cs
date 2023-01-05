namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IGroupedRecipe<TRecipe>
{
    public INamedRecipe<TRecipe> InGroup(string group);

    public INamedRecipe<TRecipe> HasNoGroup();
}
