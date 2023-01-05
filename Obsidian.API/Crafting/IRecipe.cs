namespace Obsidian.API.Crafting;

public interface IRecipe
{
    public string Name { get; set; }

    public CraftingType Type { get; init; }

    public string? Group { get; init; }

    public Ingredient Result { get; init; }
}
