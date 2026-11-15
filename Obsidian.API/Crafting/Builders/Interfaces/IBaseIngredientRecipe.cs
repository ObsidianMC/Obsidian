using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders.Interfaces;
/// <summary>
/// The step of a smithing recipe builder that takes the item upgraded.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IBaseIngredientRecipe<TRecipe>
{
    /// <summary>
    /// Adds <paramref name="items"/> to the items the recipe upgrades.
    /// </summary>
    public IUpgradeIngredientRecipe<TRecipe> WithBaseIngredient(params ItemStack[] items);
}

/// <summary>
/// The step of a smithing recipe builder that takes the material added.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface IUpgradeIngredientRecipe<TRecipe>
{
    /// <summary>
    /// Adds <paramref name="items"/> to the materials the recipe adds, like a netherite ingot.
    /// </summary>
    public INamedRecipe<TRecipe> WithUpgradeIngredient(params ItemStack[] items);
}

/// <summary>
/// The first step of a smithing recipe builder, which takes the smithing template.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public interface ITemplateIngredientRecipe<TRecipe>
{
    /// <summary>
    /// Adds <paramref name="items"/> to the smithing templates the recipe accepts.
    /// </summary>
    public IBaseIngredientRecipe<TRecipe> WithTemplateIngredient(params ItemStack[] items);
}
