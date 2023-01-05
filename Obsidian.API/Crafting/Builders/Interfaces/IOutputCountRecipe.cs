namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IOutputCountRecipe<TRecipe>
{
    public IGroupedRecipe<TRecipe> WithOutputCount(int count);
}
