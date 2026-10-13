using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The server's recipes: vanilla's, loaded first, then those registered with <see cref="IServer.RegisterRecipes"/>
/// (plugins' among them), which can be removed again. Crafting, furnaces and the recipe book all use the same set
/// (see <see cref="Current"/>).
/// </summary>
/// <remarks>
/// The recipes are static, so a server started again in the same process keeps the loaded ones.
/// </remarks>
public static partial class RecipesRegistry
{
    // Guards the registration state below; readers use the published set without it.
    private static readonly Lock gate = new();

    // Every recipe, by identifier.
    private static readonly Dictionary<string, IRecipe> registered = [];

    // The recipe book's display ids by recipe identifier. An identifier keeps its id for the life of the process,
    // like a plugin's items keep theirs, so that a reloaded plugin's recipes are the ones players' books had.
    private static readonly Dictionary<string, int> displayIds = [];

    // The recipe book's group ids by group name, assigned the same way.
    private static readonly Dictionary<string, int> groupIds = [];

    private static bool vanillaLoaded;

    private static volatile RecipeSet current = RecipeSet.Empty;

    /// <summary>
    /// Every recipe, by identifier: vanilla's and those registered since.
    /// </summary>
    public static IReadOnlyDictionary<string, IRecipe> Recipes => Current.Recipes;

    /// <summary>
    /// The current recipes. Read it once for work that has to see the same recipes throughout.
    /// </summary>
    internal static RecipeSet Current
    {
        get
        {
            EnsureVanillaLoaded();

            return current;
        }
    }

    /// <summary>
    /// Loads vanilla's recipes, if they aren't yet.
    /// </summary>
    public static Task InitializeAsync()
    {
        EnsureVanillaLoaded();

        return Task.CompletedTask;
    }

    public static IRecipeWithResult? FindRecipe(CraftingTable grid) => FindRecipe(grid, 3);

    internal static IRecipeWithResult? FindRecipe(BaseContainer grid, int width) => Current.FindRecipe(grid, width);

    /// <summary>
    /// Whether a recipe of <paramref name="type"/> (smelting, blasting or smoking) cooks <paramref name="item"/>, which
    /// a furnace of that type then takes as its ingredient.
    /// </summary>
    internal static bool IsCookingIngredient(CraftingType type, ItemStack item) =>
        Current.IsCookingIngredient(type, item);

    /// <summary>
    /// Adds <paramref name="recipes"/>, keyed by their identifiers in snake case, and publishes the new set. Those
    /// added are listed in <paramref name="added"/>, also when one fails.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A recipe's identifier is taken; the recipes before it were added.
    /// </exception>
    internal static void Register(IEnumerable<IRecipe> recipes, List<IRecipe> added)
    {
        EnsureVanillaLoaded();

        lock (gate)
        {
            try
            {
                foreach (var recipe in recipes)
                {
                    Add(Key(recipe), recipe);
                    added.Add(recipe);
                }
            }
            finally
            {
                Publish();
            }
        }
    }

    /// <summary>
    /// Removes <paramref name="recipes"/>, added with <see cref="Register"/>, unless another recipe took their
    /// identifier since, and publishes the new set. Their display ids stay reserved for them.
    /// </summary>
    internal static void Unregister(IEnumerable<IRecipe> recipes)
    {
        lock (gate)
        {
            foreach (var recipe in recipes)
            {
                var key = Key(recipe);

                if (registered.TryGetValue(key, out var existing) && ReferenceEquals(existing, recipe))
                    registered.Remove(key);
            }

            Publish();
        }
    }

    private static string Key(IRecipe recipe) => recipe.Identifier.ToSnakeCase();

    // Loads vanilla's recipes before any other, so that theirs are the first display ids, in vanilla's data order.
    private static void EnsureVanillaLoaded()
    {
        if (Volatile.Read(ref vanillaLoaded))
            return;

        lock (gate)
        {
            if (vanillaLoaded)
                return;

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Obsidian.Assets.recipes.json")!;
            var recipes = JsonSerializer.Deserialize<IRecipe[]>(stream, Globals.JsonOptions)!;

            foreach (var recipe in recipes)
                Add(recipe.Identifier, recipe);

            Publish();
            Volatile.Write(ref vanillaLoaded, true);
        }
    }

    // Under the gate.
    private static void Add(string key, IRecipe recipe)
    {
        registered.Add(key, recipe);

        if (recipe is ShapedRecipe or ShapelessRecipe)
            displayIds.TryAdd(key, displayIds.Count);
    }

    // Under the gate: builds the set from the registered recipes and makes it the current one.
    private static void Publish()
    {
        var book = new RecipeBookEntry?[displayIds.Count];

        foreach (var (key, displayId) in displayIds)
        {
            if (!registered.TryGetValue(key, out var recipe) || recipe is not IRecipeWithResult crafting)
                continue;

            // A recipe registered again under the same identifier may be of another kind, which the book doesn't show.
            if (crafting is not (ShapedRecipe or ShapelessRecipe))
                continue;

            book[displayId] = new RecipeBookEntry(displayId, crafting, GroupId(crafting.Group));
        }

        current = new RecipeSet(registered.ToFrozenDictionary(), book);
    }

    // Under the gate.
    private static int? GroupId(string? group)
    {
        if (string.IsNullOrEmpty(group))
            return null;

        if (!groupIds.TryGetValue(group, out var id))
        {
            id = groupIds.Count;
            groupIds.Add(group, id);
        }

        return id;
    }
}
