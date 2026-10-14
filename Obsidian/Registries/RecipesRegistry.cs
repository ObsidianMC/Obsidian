using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Shared vanilla recipes and process-stable book identities. Each server keeps its own registered recipes in
/// <see cref="ServerRecipeRegistry"/>; static lookups contain vanilla only.
/// </summary>
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
    /// Vanilla recipes, by identifier.
    /// </summary>
    public static IReadOnlyDictionary<string, IRecipe> Recipes => Current.Recipes;

    /// <summary>
    /// The shared vanilla recipe set.
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
    /// Whether a recipe of <paramref name="type"/> cooks <paramref name="item"/>, which a furnace of that type
    /// takes as its ingredient.
    /// </summary>
    internal static bool IsCookingIngredient(CraftingType type, ItemStack item) =>
        Current.IsCookingIngredient(type, item);

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

            // Only shaped and shapeless recipes have book displays.
            if (crafting is not (ShapedRecipe or ShapelessRecipe))
                continue;

            book[displayId] = new RecipeBookEntry(displayId, crafting, GroupId(crafting.Group));
        }

        current = new RecipeSet(registered.ToFrozenDictionary(), book);
    }

    // Identities contain no plugin objects and stay stable across servers and reloads.
    internal static RecipeSet BuildSet(
        IReadOnlyDictionary<string, IRecipe> recipes,
        IReadOnlyDictionary<string, long> revisions,
        ContentVersion content,
        IReadOnlyList<PluginRecipeValue>? pluginRecipes = null)
    {
        lock (gate)
        {
            foreach (var (key, recipe) in recipes)
            {
                if (recipe is ShapedRecipe or ShapelessRecipe)
                    displayIds.TryAdd(key, displayIds.Count);
            }

            var book = new RecipeBookEntry?[displayIds.Count];

            foreach (var (key, id) in displayIds)
            {
                if (recipes.TryGetValue(key, out var recipe) && recipe is ShapedRecipe or ShapelessRecipe)
                {
                    book[id] = new RecipeBookEntry(id, (IRecipeWithResult)recipe, GroupId(recipe.Group))
                    {
                        Revision = revisions.GetValueOrDefault(key)
                    };
                }
            }

            return new RecipeSet(recipes.ToFrozenDictionary(), book, pluginRecipes) { RequiredContent = content };
        }
    }

    internal static string BookIdentifier(int displayId)
    {
        lock (gate)
            return displayIds.FirstOrDefault(pair => pair.Value == displayId).Key ?? string.Empty;
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
