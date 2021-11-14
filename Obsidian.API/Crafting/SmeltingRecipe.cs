namespace Obsidian.API.Crafting;

public sealed class SmeltingRecipe : IRecipe
{
    public string Name { get; set; }

    public CraftingType Type { get; set; }

    public string? Group { get; set; }

    public Ingredient Result { get; set; }

    public Ingredient Ingredient { get; set; }

    public float Experience { get; set; }

    public int Cookingtime { get; set; }

    public SmeltingRecipe(string name, CraftingType type, string? group, Ingredient result, Ingredient ingredient, float experience, int cookingTime)
    {
        Name = name;
        Type = type;
        Group = group;
        Result = result;
        Ingredient = ingredient;
        Experience = experience;
        Cookingtime = cookingTime;
    }
}
