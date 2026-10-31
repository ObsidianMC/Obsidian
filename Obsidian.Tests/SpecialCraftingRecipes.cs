using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
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

    [Fact(DisplayName = "Crafting grids craft what vanilla 26.3's crafting recipes craft")]
    public void CraftsLikeVanilla()
    {
        var crafts = Capture().GetProperty("crafts").EnumerateArray();

        FixtureCases.AssertAll(crafts, Name, AssertCraftLikeVanilla);
    }

    private static void AssertCraftLikeVanilla(JsonElement row)
    {
        var recipe = row.GetProperty("recipe").GetString();
        var identifier = recipe?.TrimResourceTag(keepUnderscores: true);

        AssertCraftsLikeVanilla(RecipesRegistry.Current, row, identifier);
    }

    [Fact(DisplayName = "Data pack special recipes load like vanilla 26.3's")]
    public void PackRecipesLoadLikeVanilla()
    {
        var rows = Capture().GetProperty("recipes").EnumerateArray().Select((row, index) => (Row: row, Index: index));

        FixtureCases.AssertAll(rows, row => $"recipes[{row.Index}]", row => AssertPackRecipeLoadsLikeVanilla(row.Row));
    }

    private static void AssertPackRecipeLoadsLikeVanilla(JsonElement row)
    {
        var json = row.GetProperty("recipe");

        if (row.GetProperty("loads").GetBoolean())
            Assert.NotNull(ReadRecipe(json));
        else
            Assert.Throws<JsonException>(() => ReadRecipe(json));
    }

    [Fact(DisplayName = "Data pack special recipes craft what vanilla 26.3's craft")]
    public void PackRecipesCraftLikeVanilla()
    {
        var crafts = Capture().GetProperty("recipes").EnumerateArray()
            .Select((row, index) => (Row: row, Index: index))
            .SelectMany(row => row.Row.GetProperty("crafts").EnumerateArray()
                .Select(craft => (row.Row, row.Index, Craft: craft)));

        FixtureCases.AssertAll(
            crafts,
            row => $"recipes[{row.Index}] {Name(row.Craft)}",
            row => AssertPackRecipeCraftsLikeVanilla(row.Row, row.Craft));
    }

    private static void AssertPackRecipeCraftsLikeVanilla(JsonElement row, JsonElement craft)
    {
        var recipe = ReadRecipe(row.GetProperty("recipe"));
        var recipes = new RecipeSet(
            new Dictionary<string, IRecipe> { [recipe.Identifier] = recipe }.ToFrozenDictionary(),
            [new RecipeBookEntry(0, recipe, null)]);

        AssertCraftsLikeVanilla(recipes, craft, recipe.Identifier);
    }

    [Fact(DisplayName = "The recipe book shows special crafting recipes like vanilla 26.3")]
    public void BookEntriesMatchVanilla()
    {
        var rows = Capture().GetProperty("book").EnumerateArray().Select((row, index) => (Row: row, Index: index));

        FixtureCases.AssertAll(rows, row => $"book[{row.Index}]", row => AssertBookEntryMatchesVanilla(row.Row));
    }

    private static void AssertBookEntryMatchesVanilla(JsonElement row)
    {
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

        var expected = row.GetProperty("result");
        if (ReadStack(expected) is null)
        {
            Assert.Null(crafted);
            return;
        }

        // The whole stack as the client is sent it: its count and its component patch, removals included, which
        // stack equality doesn't compare.
        Assert.NotNull(crafted);
        Assert.Equal(identifier, crafted.RecipeIdentifier);
        Assert.Equal(expected.GetString(), Hex(crafted.Output));
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
