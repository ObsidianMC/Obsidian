namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IHasNotification<TRecipe>
{
    public IPatternedRecipe HasNotification(bool show = true);
}
