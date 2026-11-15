using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;

/// <summary>
/// Builds a <see cref="CuttingRecipe"/>: start with <see cref="Create"/>, then give the ingredient, output count,
/// group, identifier and result in that order.
/// </summary>
public sealed class CuttingRecipeBuilder : BaseRecipeBuilder<CuttingRecipe>, IIngredientRecipe<IOutputCountRecipe<CuttingRecipe>>,
    IOutputCountRecipe<CuttingRecipe>
{
    private Ingredient ingredient = new();

    /// <summary>
    /// Gets or sets the output count given to <see cref="WithOutputCount"/>.
    /// </summary>
    public int Count { get; set; }

    private CuttingRecipeBuilder() { }

    /// <summary>
    /// Starts a stonecutter recipe.
    /// </summary>
    public static IIngredientRecipe<IOutputCountRecipe<CuttingRecipe>> Create() => new CuttingRecipeBuilder();

    /// <summary>
    /// Adds <paramref name="items"/> to the stacks the stonecutter accepts, any one of which works.
    /// </summary>
    public IOutputCountRecipe<CuttingRecipe> WithIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.ingredient.Add(item);

        return this;
    }

    /// <summary>
    /// Sets the recipe's <see cref="CuttingRecipe.Count"/>.
    /// </summary>
    public IGroupedRecipe<CuttingRecipe> WithOutputCount(int count)
    {
        this.Count = count;

        return this;
    }

    /// <inheritdoc/>
    public override CuttingRecipe Build()
    {
        if (this.ingredient.Count <= 0)
            throw new InvalidOperationException("Recipe must atleast have 1 item as an ingredient");

        return new CuttingRecipe
        {
            Identifier = this.Identifier ?? throw new NullReferenceException("Name must not be null"),
            Type = CraftingType.Stonecutting,
            Group = this.Group,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
            Ingredient = this.ingredient ?? throw new NullReferenceException("Ingredient must not be null"),
            Count = this.Count
        };
    }
}
