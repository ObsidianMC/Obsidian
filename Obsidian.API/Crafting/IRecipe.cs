namespace Obsidian.API.Crafting;

public interface IRecipe
{
    public string Name { get; set; }

    public CraftingType Type { get; init; }

    public string? Group { get; init; }

    public Ingredient Result { get; init; }
}

public interface IHasRecipeCategory
{
    public CraftingBookCategory Category { get; init; }
}

public interface IHasCookingRecipeCategory
{
    public CookingBookCategory Category { get; init; }
}
