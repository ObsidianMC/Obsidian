using Obsidian.API.Crafting.Builders.Interfaces;

namespace Obsidian.API.Crafting.Builders;

public sealed class SmithingRecipeBuilder : BaseRecipeBuilder<SmithingRecipe>,
    IBaseIngredientRecipe<INamedRecipe<SmithingRecipe>>, 
    IUpgradeIngredientRecipe<INamedRecipe<SmithingRecipe>>
{
    private Ingredient @base = new();
    private Ingredient addition = new();

    private SmithingRecipeBuilder() { }

    public static IBaseIngredientRecipe<INamedRecipe<SmithingRecipe>> Create() => new SmithingRecipeBuilder();

    public IUpgradeIngredientRecipe<INamedRecipe<SmithingRecipe>> WithBaseIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.@base.Add(item);

        return this;
    }

    public INamedRecipe<SmithingRecipe> WithUpgradeIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.addition.Add(item);

        return this;
    }

    public override SmithingRecipe Build()
    {
        if (this.@base.Count <= 0)
            throw new InvalidOperationException("Base ingredients must have atleast 1 item.");

        if (this.addition.Count <= 0)
            throw new InvalidOperationException("Sub ingredients must have atleast 1 item.");

        return new SmithingRecipe
        {
            Name = this.Name ?? throw new NullReferenceException("Recipe must have a name"),
            Type = CraftingType.Smithing,
            Group = this.Group,
            Base = this.@base,
            Addition = this.addition,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set.")
        };
    }
}
