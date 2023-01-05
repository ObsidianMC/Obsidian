namespace Obsidian.API.Crafting.Builders.Interfaces;
public interface IHasIngredient<TBuilder>
{
    public TBuilder AddIngredient(params ItemStack[] items);
}
