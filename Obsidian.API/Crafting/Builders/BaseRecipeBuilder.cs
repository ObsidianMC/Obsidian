using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;
public abstract class BaseRecipeBuilder<TRecipe> : IGroupedRecipe<TRecipe>, INamedRecipe<TRecipe>, IRecipeResult<TRecipe>, 
    IRecipeBuilder<TRecipe> where TRecipe : IRecipe
{
    protected string Identifier { get; set; }

    protected string? Group { get; set; }

    protected ItemStack? Result { get; set; }

    public virtual IRecipeResult<TRecipe> WithIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        this.Identifier = identifier;

        return this;
    }

    public virtual IRecipeBuilder<TRecipe> WithResult(ItemStack result)
    {
        ArgumentNullException.ThrowIfNull(result);

        this.Result = result;

        return this;
    }

    public virtual INamedRecipe<TRecipe> InGroup(string group)
    {
        this.Group = group;

        return this;
    }

    public virtual INamedRecipe<TRecipe> HasNoGroup() => this;

    public abstract TRecipe Build();
}
