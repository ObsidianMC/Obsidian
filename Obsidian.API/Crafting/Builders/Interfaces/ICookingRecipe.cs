namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The steps of a <see cref="CookingRecipeBuilder"/> after its ingredient: experience, then cooking time.
/// </summary>
public interface ICookingRecipe
{
    /// <summary>
    /// Adds <paramref name="exp"/> to the experience the cooked item gives; calling it again adds more.
    /// </summary>
    public ICookingRecipe GivesExperience(float exp);

    /// <summary>
    /// Sets how long one item takes to cook, in ticks.
    /// </summary>
    public IGroupedRecipe<SmeltingRecipe> WithCookingTime(int time);
}
