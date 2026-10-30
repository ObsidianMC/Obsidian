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

    // The recipe book's display ids by recipe identifier and display (see RecipeBookEntry.Display). An identifier
    // keeps its ids for the life of the process, like a plugin's items keep theirs, so that a reloaded plugin's
    // recipes are the ones players' books had.
    private static readonly Dictionary<(string Identifier, int Display), int> displayIds = [];

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

    /// <summary>
    /// The crafting recipe a crafting table's grid matches, or <c>null</c>. A dye, imbue or transmute recipe's
    /// result depends on the grid's items; <see cref="IServer.EvaluateCrafting"/> gives what a grid crafts.
    /// </summary>
    public static ICraftingRecipe? FindCraftingRecipe(CraftingTable grid) => FindCraftingRecipe(grid, 3);

    /// <summary>
    /// The shaped or shapeless recipe a crafting table's grid matches, or <c>null</c>, also when it matches a recipe
    /// without a fixed result. Kept with its original signature for compiled plugins.
    /// </summary>
    [Obsolete("Finds only recipes with a fixed result. Use FindCraftingRecipe, or IServer.EvaluateCrafting for what "
        + "a grid crafts.")]
    public static IRecipeWithResult? FindRecipe(CraftingTable grid) => FindCraftingRecipe(grid) as IRecipeWithResult;

    internal static ICraftingRecipe? FindCraftingRecipe(BaseContainer grid, int width) =>
        Current.FindRecipe(grid, width);

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
        AddDisplayIds(key, recipe);
    }

    // Under the gate: builds the set from the registered recipes and makes it the current one.
    private static void Publish()
    {
        var book = Book(registered, new Dictionary<string, long>());

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
                AddDisplayIds(key, recipe);

            var book = Book(recipes, revisions);

            return new RecipeSet(recipes.ToFrozenDictionary(), book, pluginRecipes) { RequiredContent = content };
        }
    }

    internal static string BookIdentifier(int displayId)
    {
        lock (gate)
            return displayIds.FirstOrDefault(pair => pair.Value == displayId).Key.Identifier ?? string.Empty;
    }

    /// <summary>
    /// How many displays the recipe book has of <paramref name="recipe"/>: one for each number of materials a
    /// transmute recipe takes, like vanilla's map cloning shows one to eight blank maps, and one otherwise.
    /// </summary>
    internal static int DisplayCount(ICraftingRecipe recipe) => recipe is TransmuteRecipe transmute
        ? transmute.MaxMaterialCount - transmute.MinMaterialCount + 1
        : 1;

    // Under the gate. Crafting recipes have book displays.
    private static void AddDisplayIds(string key, IRecipe recipe)
    {
        if (recipe is not ICraftingRecipe crafting)
            return;

        for (int display = 0; display < DisplayCount(crafting); display++)
            displayIds.TryAdd((key, display), displayIds.Count);
    }

    // Under the gate: the book's entries by display id, null where an id's recipe or display is gone.
    private static RecipeBookEntry?[] Book(
        IReadOnlyDictionary<string, IRecipe> recipes,
        IReadOnlyDictionary<string, long> revisions)
    {
        var book = new RecipeBookEntry?[displayIds.Count];

        foreach (var ((key, display), id) in displayIds)
        {
            if (!recipes.TryGetValue(key, out var recipe) || recipe is not ICraftingRecipe crafting)
                continue;

            // A plugin's recipe registered again may take fewer materials than it did.
            if (display >= DisplayCount(crafting))
                continue;

            book[id] = new RecipeBookEntry(id, crafting, GroupId(crafting.Group))
            {
                Display = display,
                Revision = revisions.GetValueOrDefault(key)
            };
        }

        return book;
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
