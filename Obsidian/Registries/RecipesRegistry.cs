using Obsidian.API.Crafting;
using System.Reflection;

namespace Obsidian.Registries;
public static partial class RecipesRegistry
{
    public static readonly Dictionary<string, IRecipe> Recipes = [];

    public static async Task InitializeAsync()
    {
        await using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.recipes.json")!;

        var recipes = await fs.FromJsonAsync<IRecipe[]>();

        foreach(var recipe in recipes!)
        {
            Recipes.Add(recipe.Identifier, recipe);
        }
    }
}
