using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Registries;
public static partial class RecipesRegistry
{
    private static FrozenDictionary<string, List<CanonicalRecipe>> shapedRecipeLookup;
    private static FrozenDictionary<int, List<ShapelessRecipe>> shapelessRecipeLookup;

    public static readonly Dictionary<string, IRecipe> Recipes = [];

    public static async Task InitializeAsync()
    {
        await using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.recipes.json")!;

        var recipes = await fs.FromJsonAsync<IRecipe[]>();

        foreach (var recipe in recipes!)
            Recipes.Add(recipe.Identifier, recipe);

        LoadShapedRecipes();
        LoadShapelessRecipes();
    }

    public static IRecipeWithResult? FindRecipe(CraftingTable grid)
    {
        var shapedMatch = FindShapedRecipe(grid);
        if (shapedMatch != null)
            return shapedMatch;

        var shapelessMatch = FindShapelessRecipe(grid);
        return shapelessMatch ?? null;
    }

    private static ShapedRecipe? FindShapedRecipe(CraftingTable grid)
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

        if (!shapedRecipeLookup.TryGetValue(key, out var candidates))
            return null;

        foreach (var candidate in candidates)
        {
            if (!DoesGridMatchShaped(grid, anchorSlot, candidate))
                continue;

            return candidate.OriginalRecipe;
        }

        return null;
    }

    private static ShapelessRecipe? FindShapelessRecipe(CraftingTable grid)
    {
        var itemsInGrid = grid.Where(i => i != null).ToList();
        if (itemsInGrid.Count == 0)
            return null;

        if (!shapelessRecipeLookup.TryGetValue(itemsInGrid.Count, out var candidates))
            return null;

        foreach (var candidate in candidates)
        {
            if (!DoesGridMatchShapeless(itemsInGrid, candidate))
                continue;

            return candidate;
        }
        return null;
    }

    private static bool DoesGridMatchShaped(CraftingTable grid, int anchorSlot, CanonicalRecipe recipe)
    {
        foreach (var entry in recipe.IngredientsByOffset)
        {
            int offset = entry.Key;
            Ingredient requiredIngredient = entry.Value;

            int gridSlot = anchorSlot + offset;
            if (gridSlot >= 9)
                return false;

            var itemInGrid = grid[gridSlot];

            if (!requiredIngredient.CanBe(itemInGrid))
                return false;
        }

        return true;
    }

    private static bool DoesGridMatchShapeless(List<ItemStack> gridItems, ShapelessRecipe recipe)
    {
        var remainingItems = new List<ItemStack>(gridItems);

        foreach (var requiredIngredient in recipe.Ingredients)
        {
            var foundItem = remainingItems.FirstOrDefault(requiredIngredient.CanBe);

            if (foundItem != null)
                remainingItems.Remove(foundItem);
            else
                return false;
        }

        return true;
    }

    private static void LoadShapedRecipes()
    {
        var recipeKeyDictionary = new Dictionary<string, List<CanonicalRecipe>>();
        foreach (var recipe in Recipes.Values.Where(x => x is ShapedRecipe).Cast<ShapedRecipe>())
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

        shapedRecipeLookup = recipeKeyDictionary.ToFrozenDictionary();
    }

    private static void LoadShapelessRecipes()
    {
        var recipeKeyDictionary = new Dictionary<int, List<ShapelessRecipe>>();
        foreach (var recipe in Recipes.Values.Where(x => x is ShapelessRecipe).Cast<ShapelessRecipe>())
        {
            int ingredientCount = recipe.Ingredients.Count;
            if (ingredientCount == 0) continue;

            if (!recipeKeyDictionary.TryGetValue(ingredientCount, out var value))
            {
                value = [];
                recipeKeyDictionary[ingredientCount] = value;
            }

            value.Add(recipe);
        }

        shapelessRecipeLookup = recipeKeyDictionary.ToFrozenDictionary();
    }

    public record CanonicalRecipe(Dictionary<int, Ingredient> IngredientsByOffset, ShapedRecipe OriginalRecipe);
}
