namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IHasOutputCount<TRecipe>
{
    public IGroupRecipeBuilder<TRecipe> WithOutputCount(int count);
}
