using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Registries;

public static partial class RecipesRegistry
{
    private static FrozenDictionary<int, List<ShapedRecipe>> shapedRecipeLookup;
    private static FrozenDictionary<int, List<ShapelessRecipe>> shapelessRecipeLookup;

    public static readonly Dictionary<string, IRecipe> Recipes = [];

    public static async Task InitializeAsync()
    {
        await using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.recipes.json")!;
        var recipes = await fs.FromJsonAsync<IRecipe[]>();

        foreach (var recipe in recipes!)
            Recipes.Add(recipe.Identifier, recipe);

        shapedRecipeLookup = Recipes.Values.OfType<ShapedRecipe>()
            .GroupBy(recipe => recipe.Pattern.Sum(row => row.Count(symbol => symbol != ' ')))
            .ToFrozenDictionary(group => group.Key, group => group.ToList());
        shapelessRecipeLookup = Recipes.Values.OfType<ShapelessRecipe>()
            .GroupBy(recipe => recipe.Ingredients.Count)
            .ToFrozenDictionary(group => group.Key, group => group.ToList());
    }

    public static IRecipeWithResult? FindRecipe(CraftingTable grid) => FindRecipe(grid, 3);

    internal static IRecipeWithResult? FindRecipe(BaseContainer grid, int width)
    {
        var occupiedSlots = Enumerable.Range(1, width * width)
            .Where(slot => !grid[slot].IsNullOrAir() && grid[slot]!.Count > 0).ToList();
        if (occupiedSlots.Count == 0)
            return null;

        if (shapedRecipeLookup.TryGetValue(occupiedSlots.Count, out var shapedRecipes))
        {
            int minX = occupiedSlots.Min(slot => (slot - 1) % width);
            int minY = occupiedSlots.Min(slot => (slot - 1) / width);
            int maxX = occupiedSlots.Max(slot => (slot - 1) % width);
            int maxY = occupiedSlots.Max(slot => (slot - 1) / width);

            foreach (var recipe in shapedRecipes)
            {
                if (recipe.Pattern.Count != maxY - minY + 1 || recipe.Pattern[0].Length != maxX - minX + 1)
                    continue;

                if (MatchesShaped(grid, width, minX, minY, recipe, false) ||
                    MatchesShaped(grid, width, minX, minY, recipe, true))
                    return recipe;
            }
        }

        if (shapelessRecipeLookup.TryGetValue(occupiedSlots.Count, out var shapelessRecipes))
        {
            var items = occupiedSlots.Select(slot => grid[slot]!).ToList();
            foreach (var recipe in shapelessRecipes)
            {
                if (MatchesShapeless(items, recipe.Ingredients, 0, new bool[items.Count]))
                    return recipe;
            }
        }

        return null;
    }

    private static bool MatchesShaped(BaseContainer grid, int width, int minX, int minY, ShapedRecipe recipe, bool mirrored)
    {
        for (int y = 0; y < recipe.Pattern.Count; y++)
        {
            var row = recipe.Pattern[y];
            for (int x = 0; x < row.Length; x++)
            {
                char symbol = row[mirrored ? row.Length - x - 1 : x];
                var item = grid[1 + (minY + y) * width + minX + x];
                if (symbol == ' ')
                {
                    if (!item.IsNullOrAir() && item!.Count > 0)
                        return false;
                }
                else if (item.IsNullOrAir() || item!.Count <= 0 || !MatchesIngredient(recipe.Key[symbol], item))
                    return false;
            }
        }

        return true;
    }

    private static bool MatchesIngredient(Ingredient ingredient, ItemStack item) =>
        ingredient.Any(candidate => candidate.Type == item.Type);

    private static bool MatchesShapeless(List<ItemStack> items, IReadOnlyList<Ingredient> ingredients, int index, bool[] used)
    {
        if (index == ingredients.Count)
            return true;

        // Backtrack because overlapping tags may need the same item assigned to a different ingredient.
        for (int i = 0; i < items.Count; i++)
        {
            if (used[i] || !MatchesIngredient(ingredients[index], items[i]))
                continue;

            used[i] = true;
            if (MatchesShapeless(items, ingredients, index + 1, used))
                return true;
            used[i] = false;
        }

        return false;
    }

    public record CanonicalRecipe(Dictionary<int, Ingredient> IngredientsByOffset, ShapedRecipe OriginalRecipe);
}
