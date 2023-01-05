using Obsidian.API.Crafting.Builders.Interfaces;

namespace Obsidian.API.Crafting.Builders;
public abstract class BaseRecipeBuilder<TRecipe> : IRecipeGroup<TRecipe>, IRecipeName<TRecipe>, IRecipeResult<TRecipe>, 
    IRecipeBuilder<TRecipe> where TRecipe : IRecipe
{
    protected string Name { get; set; }

    protected string? Group { get; set; }

    protected ItemStack Result { get; set; }

    public virtual IRecipeResult<TRecipe> WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        this.Name = name;

        return this;
    }

    public virtual IRecipeBuilder<TRecipe> SetResult(ItemStack result)
    {
        ArgumentNullException.ThrowIfNull(result);

        this.Result = result;

        return this;
    }

    public virtual IRecipeName<TRecipe> InGroup(string group)
    {
        this.Group = group;

        return this;
    }

    public virtual IRecipeName<TRecipe> HasNoGroup() => this;

    public abstract TRecipe Build();
}
