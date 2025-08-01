using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using System.Reflection;
using System.Text;

namespace Obsidian.Registries;
public static partial class RecipesRegistry
{
    public static readonly Dictionary<string, IRecipe> Recipes = [];
    private static readonly Dictionary<string, List<ShapedRecipe>> _recipeLookup = [];

    public static async Task InitializeAsync()
    {
        await using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.recipes.json")!;

        var recipes = await fs.FromJsonAsync<IRecipe[]>();

        foreach (var recipe in recipes!)
        {
            Recipes.Add(recipe.Identifier, recipe);
        }

        foreach (var recipe in recipes.Where(x => x is ShapedRecipe).Cast<ShapedRecipe>())
        {
            // The key is now based only on the shape, not the items.
            var key = GenerateShapeKey(recipe);
            if (!_recipeLookup.TryGetValue(key, out var value))
            {
                value = [];
                _recipeLookup[key] = value;
            }

            value.Add(recipe);
        }
    }

    // The lookup dictionary now maps a shape key to a LIST of recipes.


    /// <summary>
    /// Finds a recipe matching the grid. O(1) for shape lookup, then a
    /// quick check over a very small list.
    /// </summary>
    public static ShapedRecipe? FindRecipe(CraftingTable grid)
    {
        // Phase 1: Find candidate recipes based on the shape of items in the grid.
        var shapeKey = GenerateShapeKey(grid, out int minRow, out int minCol);
        if (shapeKey == null || !_recipeLookup.TryGetValue(shapeKey, out var candidates))
            return null;

        // Phase 2: Check the ingredients for each candidate recipe.
        foreach (var candidate in candidates)
        {
            if (DoesGridMatchRecipe(grid, candidate, minRow, minCol))
                return candidate;

        }

        return null;
    }

    /// <summary>
    /// Checks if the items in the grid satisfy the ingredient requirements of a recipe.
    /// </summary>
    private static bool DoesGridMatchRecipe(CraftingTable grid, ShapedRecipe recipe, int gridMinRow, int gridMinCol)
    {
        for (int r = 0; r < recipe.Pattern.Count; r++)
        {
            for (int c = 0; c < recipe.Pattern[r].Length; c++)
            {
                char ingredientChar = recipe.Pattern[r][c];
                var itemInGrid = grid.GetItem(gridMinRow + r, gridMinCol + c);

                if (ingredientChar == ' ') // Empty space in recipe
                {
                    if (itemInGrid != null)
                        return false; // Grid has an item where recipe expects none.
                }
                else // Recipe expects an item here
                {
                    if (itemInGrid == null)
                        return false; // Grid has no item where recipe expects one.

                    // Check if the item satisfies the ingredient requirement.
                    if (!recipe.Key[ingredientChar].CanBe(itemInGrid))
                        return false;

                }
            }
        }

        return true;
    }

    // Generates a key from a raw recipe definition
    private static string GenerateShapeKey(ShapedRecipe recipe)
    {
        var keyBuilder = new StringBuilder();
        foreach (var row in recipe.Pattern)
        {
            foreach (char c in row)
            {
                // 'X' for an item, '.' for an empty space.
                keyBuilder.Append(c == ' ' ? '.' : 'X');
            }
            keyBuilder.Append(';'); // Row separator
        }
        return keyBuilder.ToString();
    }

    // Generates a key from a live crafting grid
    private static string? GenerateShapeKey(CraftingTable grid, out int minRow, out int minCol)
    {
        minRow = -1;
        minCol = -1;
        int maxRow = -1, maxCol = -1;

        // Find the bounding box of the items in the grid
        for (int i = 0; i < grid.Size; i++)
        {
            if (grid[i] != null)
            {
                int r = i / grid.Size;
                int c = i % grid.Size;
                if (minRow == -1) minRow = r;
                maxRow = r;
                if (minCol == -1 || c < minCol) minCol = c;
                if (maxCol == -1 || c > maxCol) maxCol = c;
            }
        }

        if (minRow == -1) return null; // Grid is empty

        var keyBuilder = new StringBuilder();
        for (int r = minRow; r <= maxRow; r++)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                keyBuilder.Append(grid.GetItem(r, c) != null ? 'X' : '.');
            }
            keyBuilder.Append(';');
        }

        return keyBuilder.ToString();
    }
}
