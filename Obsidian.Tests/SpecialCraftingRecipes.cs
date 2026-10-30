using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// 26.1's configurable special crafting recipes (crafting_dye, crafting_imbue and crafting_transmute) load, craft and
/// show in the recipe book like vanilla 26.3's, replayed from <c>special-recipes-26.3.json</c>
/// (<c>SpecialRecipeFixtures.java</c>).
/// </summary>
public sealed class SpecialCraftingRecipes
{
    private static readonly string[] SpecialTypes =
    [
        "minecraft:crafting_dye",
        "minecraft:crafting_imbue",
        "minecraft:crafting_transmute"
    ];

    public static TheoryData<string> Crafts => [.. Capture().GetProperty("crafts").EnumerateArray().Select(Name)];

    public static TheoryData<int> BookRecipes =>
        [.. Enumerable.Range(0, Capture().GetProperty("book").GetArrayLength())];

    public static TheoryData<int> PackRecipes =>
        [.. Enumerable.Range(0, Capture().GetProperty("recipes").GetArrayLength())];

    public static TheoryData<int, string> PackCrafts
    {
        get
        {
            var rows = new TheoryData<int, string>();
            var recipes = Capture().GetProperty("recipes").EnumerateArray().ToList();

            for (int index = 0; index < recipes.Count; index++)
            {
                foreach (var craft in recipes[index].GetProperty("crafts").EnumerateArray())
                    rows.Add(index, Name(craft));
            }

            return rows;
        }
    }

    [Theory(DisplayName = "Crafting grids craft what vanilla 26.3's crafting recipes craft")]
    [MemberData(nameof(Crafts))]
    public void CraftsLikeVanilla(string name)
    {
        var row = Capture().GetProperty("crafts").EnumerateArray().Single(craft => Name(craft) == name);
        var recipe = row.GetProperty("recipe").GetString();
        var identifier = recipe?.TrimResourceTag(keepUnderscores: true);

        AssertCraftsLikeVanilla(RecipesRegistry.Current, row, identifier);
    }

    [Theory(DisplayName = "Data pack special recipes load like vanilla 26.3's")]
    [MemberData(nameof(PackRecipes))]
    public void PackRecipesLoadLikeVanilla(int index)
    {
        var row = Capture().GetProperty("recipes")[index];
        var json = row.GetProperty("recipe");

        if (row.GetProperty("loads").GetBoolean())
            Assert.NotNull(ReadRecipe(json));
        else
            Assert.Throws<JsonException>(() => ReadRecipe(json));
    }

    [Theory(DisplayName = "Data pack special recipes craft what vanilla 26.3's craft")]
    [MemberData(nameof(PackCrafts))]
    public void PackRecipesCraftLikeVanilla(int index, string name)
    {
        var row = Capture().GetProperty("recipes")[index];
        var recipe = ReadRecipe(row.GetProperty("recipe"));
        var craft = row.GetProperty("crafts").EnumerateArray().Single(craft => Name(craft) == name);
        var recipes = new RecipeSet(
            new Dictionary<string, IRecipe> { [recipe.Identifier] = recipe }.ToFrozenDictionary(),
            [new RecipeBookEntry(0, recipe, null)]);

        AssertCraftsLikeVanilla(recipes, craft, recipe.Identifier);
    }

    [Theory(DisplayName = "The recipe book shows special crafting recipes like vanilla 26.3")]
    [MemberData(nameof(BookRecipes))]
    public void BookEntriesMatchVanilla(int index)
    {
        var row = Capture().GetProperty("book")[index];
        var recipe = ReadRecipe(row.GetProperty("recipe"));
        int? group = string.IsNullOrEmpty(recipe.Group) ? null : 0;
        var entries = Enumerable.Range(0, RecipesRegistry.DisplayCount(recipe))
            .Select(display => new RecipeBookEntry(display, recipe, group) { Display = display })
            .ToList();

        using var buffer = new NetworkBuffer();
        new RecipeBookAddPacket(entries, replace: false).Serialize(buffer);

        var expected = row.GetProperty("packet").GetString()!;
        Assert.Equal(expected, Convert.ToHexStringLower(buffer.AsSpan(0, buffer.Size)));
    }

    [Fact(DisplayName = "Every vanilla dye, imbue and transmute recipe loads, with a book entry for each display")]
    public void VanillaSpecialRecipesLoad()
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream("Obsidian.Assets.recipes.json")!;
        using var vanilla = JsonDocument.Parse(stream);
        var expected = vanilla.RootElement.EnumerateObject()
            .Where(recipe => SpecialTypes.Contains(recipe.Value.GetProperty("type").GetString()))
            .Select(recipe => recipe.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var recipes = RecipesRegistry.Current;
        var loaded = expected
            .Where(name => recipes.Recipes.GetValueOrDefault(name) is DyeRecipe or ImbueRecipe or TransmuteRecipe)
            .ToList();
        var displays = recipes.Book
            .Where(entry => entry is not null)
            .GroupBy(entry => entry!.Recipe.Identifier)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(expected, loaded);
        Assert.All(loaded, name =>
            Assert.Equal(RecipesRegistry.DisplayCount((ICraftingRecipe)recipes.Recipes[name]), displays[name]));
        Assert.Equal(8, displays["map_cloning"]);
    }

    // Evaluates a captured grid with the recipes, expecting what vanilla crafted with the recipe named identifier.
    // A recipe that matched but assembled an invalid stack crafted nothing (an empty result).
    private static void AssertCraftsLikeVanilla(RecipeSet recipes, JsonElement row, string? identifier)
    {
        var grid = row.GetProperty("grid").EnumerateArray().Select(ReadStack).ToArray();
        var width = row.GetProperty("width").GetInt32();

        var crafted = recipes.EvaluateCrafting(grid, width);

        var expected = ReadStack(row.GetProperty("result"));
        if (expected is null)
        {
            Assert.Null(crafted);
            return;
        }

        Assert.NotNull(crafted);
        Assert.Equal(identifier, crafted.RecipeIdentifier);
        Assert.Equal(expected.Count, crafted.Output.Count);
        Assert.True(expected == crafted.Output, $"Obsidian crafted {Hex(crafted.Output)}, vanilla {Hex(expected)}.");
    }

    private static JsonElement Capture()
    {
        var assembly = typeof(Server).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.special-recipes-26.3.json")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }

    private static string Name(JsonElement craft) => craft.GetProperty("name").GetString()!;

    // A stack as vanilla's optional item stack stream codec wrote it; null is an empty slot.
    private static ItemStack? ReadStack(JsonElement hex)
    {
        if (hex.ValueKind == JsonValueKind.Null)
            return null;

        using var reader = new NetworkBuffer(Convert.FromHexString(hex.GetString()!));
        return reader.ReadItemStack();
    }

    private static string Hex(ItemStack stack)
    {
        using var writer = new NetworkBuffer();
        writer.WriteItemStack(stack);

        return Convert.ToHexStringLower(writer.AsSpan(0, writer.Size));
    }

    // Reads one recipe as recipes.json and plugins' recipe files do.
    private static ICraftingRecipe ReadRecipe(JsonElement json)
    {
        var recipes = new Dictionary<string, JsonElement> { ["probe:recipe"] = json };
        var serialized = JsonSerializer.Serialize(recipes);

        return (ICraftingRecipe)Assert.Single(JsonSerializer.Deserialize<IRecipe[]>(serialized, Globals.JsonOptions)!);
    }
}
