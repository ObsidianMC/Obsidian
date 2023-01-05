namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface ISmeltingRecipeBuilder
{
    public ISmeltingRecipeBuilder GivesExperience(float exp);

    public IGroupRecipeBuilder<SmeltingRecipe> WithCookingTime(int time);
}
