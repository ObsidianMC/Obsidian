namespace Obsidian.API.Crafting;

/// <summary>
/// A recipe the server knows, read from vanilla's data or registered by a plugin (see
/// <see cref="IServer.RegisterRecipes"/>).
/// </summary>
public interface IRecipe
{
    /// <summary>
    /// Gets the recipe's identifier, like <c>minecraft:oak_planks</c>, which recipe books and commands refer to it by.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the kind of recipe, which decides where it's crafted and how it's sent to clients (vanilla's <c>type</c>).
    /// </summary>
    public CraftingType Type { get; init; }

    /// <summary>
    /// Gets the recipe book group: recipes with the same group show as one entry. Null or empty for none (vanilla's
    /// <c>group</c>).
    /// </summary>
    public string? Group { get; init; }
}

/// <summary>
/// A recipe with a fixed result.
/// </summary>
public interface IRecipeWithResult : IRecipe
{
    /// <summary>
    /// Gets the result, an ingredient holding the one stack crafted, with its count (vanilla's <c>result</c>).
    /// </summary>
    public Ingredient Result { get; init; }
}

/// <summary>
/// A recipe crafting grids craft, which the recipe book shows: <see cref="ShapedRecipe"/>,
/// <see cref="ShapelessRecipe"/>, or one of 26.1's configurable special recipes (<see cref="DyeRecipe"/>,
/// <see cref="ImbueRecipe"/> and <see cref="TransmuteRecipe"/>), whose result depends on the grid's items.
/// </summary>
public interface ICraftingRecipe : IRecipe
{
    /// <summary>The recipe book tab the recipe shows in.</summary>
    public CraftingBookCategory Category { get; }
}
