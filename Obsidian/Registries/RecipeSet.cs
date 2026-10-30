using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Frozen;

namespace Obsidian.Registries;

/// <summary>
/// The recipes at one moment, vanilla's and those registered since (plugins' among them): what crafting grids and
/// furnaces match, and what players' recipe books show. A set never changes; each change to the recipes publishes a
/// new one (see <see cref="ServerRecipeRegistry.Current"/>), so reading one set keeps a consistent view.
/// </summary>
internal sealed class RecipeSet
{
    private static long nextRevision;

    internal long Revision { get; } = System.Threading.Interlocked.Increment(ref nextRevision);

    public static readonly RecipeSet Empty = new(FrozenDictionary<string, IRecipe>.Empty, []);

    private readonly FrozenDictionary<int, List<IRecipeWithResult>> craftingRecipes;

    private readonly SmeltingRecipe[] cookingRecipes;
    private readonly FrozenDictionary<(CraftingType, int), SmeltingRecipe[]> cookingInputs;

    private readonly BrewingRecipe[] brewingRecipes;
    private readonly FrozenSet<int> brewingInputs;
    private readonly FrozenSet<int> brewingReagents;

    /// <param name="recipes">Every recipe, by identifier.</param>
    /// <param name="book">The recipe book's entries by display id, <c>null</c> where an id's recipe is gone.</param>
    public RecipeSet(
        FrozenDictionary<string, IRecipe> recipes,
        IReadOnlyList<RecipeBookEntry?> book,
        IReadOnlyList<PluginRecipeValue>? pluginRecipes = null)
    {
        this.Recipes = recipes;
        this.Book = book;
        this.pluginRecipes = (pluginRecipes ?? [])
            .OrderBy(recipe => recipe.Identifier, StringComparer.Ordinal)
            .GroupBy(recipe => recipe.Binding.Identifier)
            .ToFrozenDictionary(group => group.Key, group => group.ToArray());

        // Grids try crafting recipes in display id order, so vanilla's come first and a plugin's recipe keeps its
        // place across reloads.
        var crafting = book
            .Where(entry => entry is not null)
            .Select(entry => entry!.Recipe)
            .ToList();

        this.craftingRecipes = crafting
            .GroupBy(recipe => recipe is ShapedRecipe shaped
                ? shaped.Pattern.Sum(row => row.Count(symbol => symbol != ' '))
                : ((ShapelessRecipe)recipe).Ingredients.Count)
            .ToFrozenDictionary(group => group.Key, group => group.ToList());
        this.cookingRecipes = [.. recipes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value).OfType<SmeltingRecipe>()];
        this.cookingInputs = this.cookingRecipes
            .SelectMany(recipe => recipe.Ingredient
                .Select(item => ((recipe.Type, (int)item.Holder.Id), Recipe: recipe)))
            .GroupBy(pair => pair.Item1)
            .ToFrozenDictionary(group => group.Key, group => group.Select(pair => pair.Recipe).Distinct().ToArray());

        // Vanilla's recipe manager tries recipes of a type in identifier order, and its brewing_input and
        // brewing_reagent property sets are the items of every brewing recipe's input and reagent.
        this.brewingRecipes = [.. recipes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value).OfType<BrewingRecipe>()];
        this.brewingInputs = this.brewingRecipes
            .SelectMany(recipe => recipe.Input.Items)
            .Select(item => (int)item.Holder.Id)
            .ToFrozenSet();
        this.brewingReagents = this.brewingRecipes
            .SelectMany(recipe => recipe.Reagent.Items)
            .Select(item => (int)item.Holder.Id)
            .ToFrozenSet();

        this.UpdatePacket = new(recipes);
    }

    /// <summary>
    /// Every recipe, by identifier.
    /// </summary>
    public FrozenDictionary<string, IRecipe> Recipes { get; }

    internal Net.Packets.Play.Clientbound.UpdateRecipesPacket UpdatePacket { get; }

    private readonly FrozenDictionary<string, PluginRecipeValue[]> pluginRecipes;

    internal TRecipe? FindPluginRecipe<TRecipe>(RecipeType<TRecipe> type, IReadOnlyList<ItemStack?> inputs)
        where TRecipe : class
    {
        if (!this.pluginRecipes.TryGetValue(type.Identifier, out var recipes))
            return null;

        foreach (var recipe in recipes)
        {
            if (!recipe.Binding.Active || recipe.Owner.IsClosing || recipe.Value is not TRecipe value)
                continue;

            using var call = recipe.Binding.Owner.TryEnter();
            if (call is not null && recipe.Binding.Matches(value, inputs))
                return value;
        }

        return null;
    }

    /// <summary>
    /// The recipes of a plugin's type whose plugin is loaded, in lexical identifier order, like
    /// <see cref="FindPluginRecipe"/> tries them.
    /// </summary>
    internal IReadOnlyList<TRecipe> GetPluginRecipes<TRecipe>(RecipeType<TRecipe> type)
        where TRecipe : class
    {
        if (!this.pluginRecipes.TryGetValue(type.Identifier, out var recipes))
            return [];

        return
        [
            .. recipes
                .Where(recipe => recipe.Binding.Active && !recipe.Owner.IsClosing)
                .Select(recipe => recipe.Value)
                .OfType<TRecipe>()
        ];
    }

    /// <summary>The configured content when this set was published; null for vanilla's shared set.</summary>
    internal ContentVersion? RequiredContent { get; init; }

    /// <summary>
    /// The recipe book's entries by display id: the crafting recipes, which every player knows. An id stays its
    /// recipe's for the life of the process, so the slot of a recipe that's gone (its plugin unloaded) is
    /// <c>null</c>, and the recipe gets it back when it's registered again.
    /// </summary>
    public IReadOnlyList<RecipeBookEntry?> Book { get; }

    /// <summary>
    /// The recipe book's entry with <paramref name="displayId"/>, or <c>null</c> if there's none now.
    /// </summary>
    public RecipeBookEntry? GetBookEntry(int displayId) =>
        displayId >= 0 && displayId < this.Book.Count ? this.Book[displayId] : null;

    /// <summary>
    /// The recipe the ingredients in slots 1 to <paramref name="width"/>² of <paramref name="grid"/> craft, shaped
    /// and shapeless recipes in display id order (shaped recipes mirrored too), or <c>null</c>.
    /// </summary>
    public IRecipeWithResult? FindRecipe(BaseContainer grid, int width)
    {
        return this.FindCraftingRecipe(Enumerable.Range(1, width * width).Select(slot => grid[slot]).ToArray(), width);
    }

    /// <summary>Evaluates caller-owned copies against this single snapshot.</summary>
    internal CraftingEvaluation? EvaluateCrafting(IReadOnlyList<ItemStack?> grid, int width)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (width is not (2 or 3) || grid.Count != width * width)
            throw new ArgumentException("A crafting grid must contain width squared slots, with width 2 or 3.");

        var inputs = grid.Select(item => item is null ? null : new ItemStack(item, item.Count)).ToArray();
        var recipe = this.FindCraftingRecipe(inputs, width);
        if (recipe?.Result.FirstOrDefault() is not ItemStack output)
            return null;

        var consumed = inputs.Select(item => item.IsNullOrAir() || item!.Count <= 0 ? 0 : 1).ToArray();
        var remainders = inputs.Select(CraftingRemainder).ToArray();
        return new CraftingEvaluation(new ItemStack(output, output.Count), recipe.Identifier,
            Array.AsReadOnly(consumed), Array.AsReadOnly(remainders), this.Revision);
    }

    /// <summary>Vanilla CraftingRecipe.getRemainingItems, shared with result-slot consumption.</summary>
    internal static ItemStack? CraftingRemainder(ItemStack? ingredient)
    {
        if (ingredient.IsNullOrAir() || ingredient!.Count <= 0)
            return null;

        return ingredient.Holder.Id switch
        {
            ItemIds.MilkBucket or ItemIds.WaterBucket or ItemIds.LavaBucket
                => new ItemStack(ItemsRegistry.Bucket, 1),
            ItemIds.HoneyBottle => new ItemStack(ItemsRegistry.GlassBottle, 1),
            _ => null
        };
    }

    private IRecipeWithResult? FindCraftingRecipe(IReadOnlyList<ItemStack?> grid, int width)
    {
        var occupiedSlots = Enumerable.Range(0, width * width)
            .Where(slot => !grid[slot].IsNullOrAir() && grid[slot]!.Count > 0)
            .ToList();
        if (occupiedSlots.Count == 0)
            return null;

        if (!this.craftingRecipes.TryGetValue(occupiedSlots.Count, out var candidates))
            return null;

        int minX = occupiedSlots.Min(slot => slot % width);
        int minY = occupiedSlots.Min(slot => slot / width);
        int maxX = occupiedSlots.Max(slot => slot % width);
        int maxY = occupiedSlots.Max(slot => slot / width);
        var items = occupiedSlots.Select(slot => grid[slot]!).ToList();

        foreach (var recipe in candidates)
        {
            if (recipe is ShapedRecipe shaped)
            {
                if (shaped.Pattern.Count != maxY - minY + 1 || shaped.Pattern[0].Length != maxX - minX + 1)
                    continue;

                if (MatchesShaped(grid, width, minX, minY, shaped, false)
                    || MatchesShaped(grid, width, minX, minY, shaped, true))
                {
                    return recipe;
                }
            }
            else if (recipe is ShapelessRecipe shapeless
                && MatchesShapeless(items, shapeless.Ingredients, 0, new bool[items.Count]))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a recipe of <paramref name="type"/> cooks <paramref name="item"/>, which a furnace of that type
    /// takes as its ingredient.
    /// </summary>
    public bool IsCookingIngredient(CraftingType type, ItemStack item) =>
        this.cookingInputs.ContainsKey((type, item.Holder.Id));

    /// <summary>
    /// Whether <paramref name="item"/> is some brewing recipe's input item (vanilla's <c>brewing_input</c> property
    /// set).
    /// </summary>
    public bool IsBrewingInput(ItemStack item) => this.brewingInputs.Contains(item.Holder.Id);

    /// <summary>
    /// Whether <paramref name="item"/> is some brewing recipe's reagent item (vanilla's <c>brewing_reagent</c>
    /// property set), which a brewing stand's ingredient slot takes.
    /// </summary>
    public bool IsBrewingReagent(ItemStack item) => this.brewingReagents.Contains(item.Holder.Id);

    /// <summary>
    /// The first brewing recipe that brews <paramref name="bottle"/> with <paramref name="reagent"/>, if any.
    /// </summary>
    public BrewingRecipe? FindBrewing(ItemStack bottle, ItemStack reagent) =>
        this.brewingRecipes.FirstOrDefault(recipe => recipe.Matches(bottle, reagent));

    /// <summary>Tries the last matching identifier first, like vanilla's cached cooking lookup.</summary>
    internal SmeltingRecipe? FindCooking(CraftingType type, ItemStack item, string? previous = null)
    {
        if (previous is not null
            && this.Recipes.GetValueOrDefault(previous) is SmeltingRecipe cached
            && cached.Type == type
            && MatchesIngredient(cached.Ingredient, item))
        {
            return cached;
        }

        return this.cookingInputs.TryGetValue((type, item.Holder.Id), out var candidates)
            ? candidates.FirstOrDefault()
            : null;
    }

    private static bool MatchesShaped(
        IReadOnlyList<ItemStack?> grid,
        int width,
        int minX,
        int minY,
        ShapedRecipe recipe,
        bool mirrored)
    {
        for (int y = 0; y < recipe.Pattern.Count; y++)
        {
            var row = recipe.Pattern[y];
            for (int x = 0; x < row.Length; x++)
            {
                char symbol = row[mirrored ? row.Length - x - 1 : x];
                var item = grid[(minY + y) * width + minX + x];
                if (symbol == ' ')
                {
                    if (!item.IsNullOrAir() && item!.Count > 0)
                        return false;
                }
                else if (item.IsNullOrAir() || item!.Count <= 0 || !MatchesIngredient(recipe.Key[symbol], item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool MatchesIngredient(Ingredient ingredient, ItemStack item) =>
        ingredient.Any(candidate => candidate.Holder == item.Holder);

    private static bool MatchesShapeless(
        List<ItemStack> items,
        IReadOnlyList<Ingredient> ingredients,
        int index,
        bool[] used)
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
}

/// <summary>
/// A crafting recipe as the recipe book shows it.
/// </summary>
/// <param name="DisplayId">The id the client knows it by, in the recipe book packets and when placing it.</param>
/// <param name="Recipe">The shaped or shapeless recipe.</param>
/// <param name="Group">
/// The id of its group, which the client shows as one entry, or <c>null</c> when it has none. Recipes with the same
/// group name share the id, whoever registered them, like vanilla's.
/// </param>
internal sealed record RecipeBookEntry(int DisplayId, IRecipeWithResult Recipe, int? Group)
{
    /// <summary>The registration's revision, used to detect replacement without keeping old recipes alive.</summary>
    public long Revision { get; init; }

    /// <summary>The recipe book tab the recipe shows in.</summary>
    public CraftingBookCategory Category => this.Recipe is ShapedRecipe shaped
        ? shaped.Category
        : ((ShapelessRecipe)this.Recipe).Category;
}
