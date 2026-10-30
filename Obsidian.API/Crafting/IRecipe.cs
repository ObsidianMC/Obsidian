namespace Obsidian.API.Crafting;

public interface IRecipe
{
    public string Identifier { get; }

    public CraftingType Type { get; init; }

    public string? Group { get; init; }
}

public interface IRecipeWithResult : IRecipe
{
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
