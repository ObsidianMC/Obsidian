namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface ICookingRecipe
{
    public ICookingRecipe GivesExperience(float exp);

    public IGroupedRecipe<SmeltingRecipe> WithCookingTime(int time);
}
