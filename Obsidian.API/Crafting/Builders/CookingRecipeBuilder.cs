using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting.Builders;

/// <summary>
/// Builds a <see cref="SmeltingRecipe"/>: start with <see cref="Create"/>, then give the ingredient, experience,
/// cooking time, group, identifier and result in that order.
/// </summary>
public sealed class CookingRecipeBuilder : BaseRecipeBuilder<SmeltingRecipe>, IIngredientRecipe<ICookingRecipe>, ICookingRecipe
{
    private readonly CookingBookCategory category;

    private readonly SmeltingType type;

    private Ingredient ingredient = new();

    private float experience;

    private int cookingTime;

    private CookingRecipeBuilder(CookingBookCategory category, SmeltingType type)
    {
        this.category = category;
        this.type = type;
    }

    /// <summary>
    /// Starts a cooking recipe for the recipe book tab <paramref name="category"/>, cooked in the block
    /// <paramref name="type"/> names.
    /// </summary>
    public static IIngredientRecipe<ICookingRecipe> Create(CookingBookCategory category, SmeltingType type) =>
        new CookingRecipeBuilder(category, type);

    /// <inheritdoc/>
    public ICookingRecipe WithIngredient(params ItemStack[] items)
    {
        foreach (var item in items)
            this.ingredient.Add(item);

        return this;
    }

    /// <inheritdoc/>
    public ICookingRecipe GivesExperience(float exp)
    {
        this.experience += exp;//Just add if this function gets called again

        return this;
    }

    /// <inheritdoc/>
    public IGroupedRecipe<SmeltingRecipe> WithCookingTime(int cookingTime)
    {
        this.cookingTime = cookingTime;

        return this;
    }
    /// <inheritdoc/>
    public override SmeltingRecipe Build()
    {
        CraftingType type = this.type switch
        {
            SmeltingType.Default => CraftingType.Smelting,
            SmeltingType.Blasting => CraftingType.Blasting,
            SmeltingType.Smoking => CraftingType.Smoking,
            SmeltingType.CampfireCooking => CraftingType.CampfireCooking,
            _ => throw new NotImplementedException()
        };

        if (this.ingredient.Count <= 0)
            throw new InvalidOperationException("Recipe must atleast have 1 item as an ingredient");

        return new SmeltingRecipe
        {
            Identifier = this.Identifier ?? throw new NullReferenceException("Recipe must have a name"),
            Type = type,
            Group = this.Group,
            Result = this.Result != null ? new Ingredient { this.Result } : throw new NullReferenceException("Result is not set."),
            Ingredient = this.ingredient,
            Experience = this.experience,
            CookingTime = this.cookingTime,
            Category = this.category
        };
    }
}

/// <summary>
/// Which block a <see cref="CookingRecipeBuilder"/> recipe cooks in.
/// </summary>
public enum SmeltingType
{
    /// <summary>
    /// A furnace (<see cref="CraftingType.Smelting"/>).
    /// </summary>
    Default,
    /// <summary>
    /// A blast furnace (<see cref="CraftingType.Blasting"/>).
    /// </summary>
    Blasting,
    /// <summary>
    /// A smoker (<see cref="CraftingType.Smoking"/>).
    /// </summary>
    Smoking,
    /// <summary>
    /// A campfire (<see cref="CraftingType.CampfireCooking"/>).
    /// </summary>
    CampfireCooking
}
