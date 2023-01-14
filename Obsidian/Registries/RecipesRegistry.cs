using Obsidian.API.Crafting;

namespace Obsidian.Registries;
public static class RecipesRegistry
{
    public static readonly Dictionary<string, IRecipe> Recipes = new();
}
