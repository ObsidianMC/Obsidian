using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Registries;
public static partial class RecipesRegistry
{
    public static readonly Dictionary<string, IRecipe> Recipes = [];
    private static FrozenDictionary<string, List<CanonicalRecipe>> recipeLookup;

    public static async Task InitializeAsync()
    {
        await using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.recipes.json")!;

        var recipes = await fs.FromJsonAsync<IRecipe[]>();

        foreach (var recipe in recipes!)
            Recipes.Add(recipe.Identifier, recipe);

        var recipeKeyDictionary = new Dictionary<string, List<CanonicalRecipe>>();
        foreach (var recipe in recipes.Where(x => x is ShapedRecipe).Cast<ShapedRecipe>())
        {
            var occupiedSlots = new List<int>();
            var ingredientsBySlot = new Dictionary<int, Ingredient>();

            for (int r = 0; r < recipe.Pattern.Count; r++)
            {
                for (int c = 0; c < recipe.Pattern[r].Length; c++)
                {
                    if (recipe.Pattern[r][c] != ' ')
                    {
                        int slot = r * 3 + c;
                        occupiedSlots.Add(slot);
                        ingredientsBySlot[slot] = recipe.Key[recipe.Pattern[r][c]];
                    }
                }
            }

            if (occupiedSlots.Count == 0)
                continue;

            int anchorSlot = occupiedSlots.Min();

            var ingredientsByOffset = new Dictionary<int, Ingredient>();
            foreach (int slot in occupiedSlots)
                ingredientsByOffset[slot - anchorSlot] = ingredientsBySlot[slot];

            var canonicalRecipe = new CanonicalRecipe(ingredientsByOffset, recipe);

            var key = string.Join(":", ingredientsByOffset.Keys.OrderBy(k => k));

            if (!recipeKeyDictionary.ContainsKey(key))
                recipeKeyDictionary[key] = [];

            recipeKeyDictionary[key].Add(canonicalRecipe);
        }

        recipeLookup = recipeKeyDictionary.ToFrozenDictionary();
    }

    public static ShapedRecipe? FindRecipe(CraftingTable grid)
    {
        var occupiedSlots = new List<int>();
        for (int i = 0; i < 9; i++)
        {
            if (grid[i] != null)
                occupiedSlots.Add(i);
        }

        if (occupiedSlots.Count == 0) 
            return null;

        int anchorSlot = occupiedSlots.Min();
        var relativeOffsets = occupiedSlots.Select(s => s - anchorSlot).OrderBy(o => o);
        var key = string.Join(":", relativeOffsets);

        if (!recipeLookup.TryGetValue(key, out var candidates))
            return null;

        foreach (var candidate in candidates)
        {
            if (!DoesGridMatchIngredients(grid, anchorSlot, candidate))
                continue;

            return candidate.OriginalRecipe;
        }

        return null;
    }

    private static bool DoesGridMatchIngredients(CraftingTable grid, int anchorSlot, CanonicalRecipe recipe)
    {
        foreach (var entry in recipe.IngredientsByOffset)
        {
            int offset = entry.Key;
            Ingredient requiredIngredient = entry.Value;
            var itemInGrid = grid[anchorSlot + offset];

            if (!requiredIngredient.CanBe(itemInGrid))
                return false;
        }

        return true;
    }
    public record CanonicalRecipe(Dictionary<int, Ingredient> IngredientsByOffset, ShapedRecipe OriginalRecipe);
}
