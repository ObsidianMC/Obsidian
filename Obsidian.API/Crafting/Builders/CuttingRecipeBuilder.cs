using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;

public sealed class CuttingRecipeBuilder : BaseRecipeBuilder<CuttingRecipe>, IIngredientRecipe<IOutputCountRecipe<CuttingRecipe>>, IOutputCountRecipe<CuttingRecipe>
{
    private Ingredient ingredient = new();

    public int Count { get; set; }

    private CuttingRecipeBuilder() { }

    public static IIngredientRecipe<IOutputCountRecipe<CuttingRecipe>> Create() => new CuttingRecipeBuilder();

    public IOutputCountRecipe<CuttingRecipe> WithIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.ingredient.Add(item);

        return this;
    }

    public IGroupedRecipe<CuttingRecipe> WithOutputCount(int count)
    {
        this.Count = count;

        return this;
    }

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
