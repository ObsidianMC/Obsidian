namespace Obsidian.API.Crafting;

/// <summary>
/// A cooking recipe for a furnace, blast furnace, smoker or campfire, as <see cref="IRecipe.Type"/> says
/// (<c>minecraft:smelting</c> and the like).
/// </summary>
public sealed class SmeltingRecipe : IRecipeWithResult
{
    /// <inheritdoc/>
    public required string Identifier { get; init; }

    /// <inheritdoc/>
    public required CraftingType Type { get; init; }

    /// <inheritdoc/>
    public string? Group { get; init; }

    /// <inheritdoc/>
    public required Ingredient Result { get; init; }

    /// <summary>
    /// Gets the items the recipe cooks, any one of which works (vanilla's <c>ingredient</c>).
    /// </summary>
    public required Ingredient Ingredient { get; init; }

    /// <summary>
    /// Gets the experience the cooked item gives when taken out (vanilla's <c>experience</c>).
    /// </summary>
    public required float Experience { get; init; }

    /// <summary>
    /// Gets how long one item takes to cook, in ticks (vanilla's <c>cookingtime</c>).
    /// </summary>
    public required int CookingTime { get; init; }

    /// <summary>
    /// Gets the recipe book tab the recipe shows in (vanilla's <c>category</c>).
    /// </summary>
    public required CookingBookCategory Category { get; init; }

    internal SmeltingRecipe() { }
}
