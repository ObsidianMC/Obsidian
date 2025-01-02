using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;

public sealed class SmithingTransformRecipeBuilder : BaseRecipeBuilder<SmithingTransformRecipe>,
    IBaseIngredientRecipe<SmithingTransformRecipe>, 
    IUpgradeIngredientRecipe<SmithingTransformRecipe>,
    ITemplateIngredientRecipe<SmithingTransformRecipe>
{
    private Ingredient @base = [];
    private Ingredient addition = [];
    private Ingredient template = [];

    private SmithingTransformRecipeBuilder() { }

    public static ITemplateIngredientRecipe<SmithingTransformRecipe> Create() => new SmithingTransformRecipeBuilder();

    public IBaseIngredientRecipe<SmithingTransformRecipe> WithTemplateIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.template.Add(item);

        return this;
    }

    public IUpgradeIngredientRecipe<SmithingTransformRecipe> WithBaseIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.@base.Add(item);

        return this;
    }

    public INamedRecipe<SmithingTransformRecipe> WithUpgradeIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.addition.Add(item);

        return this;
    }

    public override SmithingTransformRecipe Build()
    {
        if (this.@base.Count <= 0)
            throw new InvalidOperationException("Base ingredients must have atleast 1 item.");

        if (this.addition.Count <= 0)
            throw new InvalidOperationException("Sub ingredients must have atleast 1 item.");

        return new SmithingTransformRecipe
        {
            Identifier = this.Identifier ?? throw new NullReferenceException("Recipe must have a name"),
            Type = CraftingType.SmithingTransform,
            Group = this.Group,
            Base = this.@base,
            Addition = this.addition,
            Template = template,
            Result = this.Result != null ? [this.Result] : throw new NullReferenceException("Result is not set.")
        };
    }
}
