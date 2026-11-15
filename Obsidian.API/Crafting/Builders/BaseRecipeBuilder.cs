using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;
/// <summary>
/// The steps every recipe builder shares: a group, an identifier, a result and the final <see cref="Build"/>.
/// </summary>
/// <typeparam name="TRecipe">The recipe built.</typeparam>
public abstract class BaseRecipeBuilder<TRecipe> : IGroupedRecipe<TRecipe>, INamedRecipe<TRecipe>, IRecipeResult<TRecipe>, 
    IRecipeBuilder<TRecipe> where TRecipe : IRecipe
{
    /// <summary>
    /// Gets or sets the identifier given to <see cref="WithIdentifier"/>.
    /// </summary>
    protected string Identifier { get; set; }

    /// <summary>
    /// Gets or sets the recipe book group given to <see cref="InGroup"/>, or null for none.
    /// </summary>
    protected string? Group { get; set; }

    /// <summary>
    /// Gets or sets the stack given to <see cref="WithResult"/>.
    /// </summary>
    protected ItemStack? Result { get; set; }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is null.</exception>
    public virtual IRecipeResult<TRecipe> WithIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        this.Identifier = identifier;

        return this;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public virtual IRecipeBuilder<TRecipe> WithResult(ItemStack result)
    {
        ArgumentNullException.ThrowIfNull(result);

        this.Result = result;

        return this;
    }

    /// <inheritdoc/>
    public virtual INamedRecipe<TRecipe> InGroup(string group)
    {
        this.Group = group;

        return this;
    }

    /// <inheritdoc/>
    /// <remarks>It only returns this builder, so a group set earlier with <see cref="InGroup"/> stays.</remarks>
    public virtual INamedRecipe<TRecipe> HasNoGroup() => this;

    /// <inheritdoc/>
    /// <exception cref="NullReferenceException">No identifier or result was given.</exception>
    /// <exception cref="InvalidOperationException">A required ingredient or pattern is missing.</exception>
    public abstract TRecipe Build();
}
