using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using System.Text.Json;

namespace EngineSample.Grinder;

/// <summary>
/// A <c>sample:grinding</c> recipe: the grinder turns one <paramref name="Ingredient"/> into <paramref name="Count"/>
/// of <paramref name="Result"/> over <paramref name="Time"/> ticks.
/// </summary>
/// <param name="Identifier">The recipe's identifier, from its file's path.</param>
public sealed record GrindingRecipe(string Identifier, Item Ingredient, Item Result, int Count, int Time);

/// <summary>
/// Reads <c>sample:grinding</c> recipes from the plugin's <c>data/sample/recipe</c> files, like
/// <c>data/sample/recipe/iron_ore_grinding.json</c>, and matches the grinder's input against them.
/// </summary>
/// <example>
/// <code>
/// {
///   "type": "sample:grinding",
///   "ingredient": "minecraft:iron_ore",
///   "result": { "id": "minecraft:raw_iron", "count": 2 },
///   "grinding_time": 20
/// }
/// </code>
/// </example>
public sealed class GrindingRecipeSerializer : IRecipeSerializer<GrindingRecipe>
{
    // A recipe without a grinding time takes a second.
    private const int DefaultTime = 20;

    /// <remarks>
    /// Throws on a malformed file, which the server logs and leaves out.
    /// </remarks>
    public GrindingRecipe Read(string identifier, JsonElement json)
    {
        var result = json.GetProperty("result");
        var count = result.TryGetProperty("count", out var counted) ? counted.GetInt32() : 1;
        var time = json.TryGetProperty("grinding_time", out var timed) ? timed.GetInt32() : DefaultTime;

        if (count <= 0 || time <= 0)
            throw new JsonException($"{identifier}: the count and grinding time must be positive.");

        return new GrindingRecipe(
            identifier,
            ReadItem(json.GetProperty("ingredient")),
            ReadItem(result.GetProperty("id")),
            count,
            time);
    }

    public bool Matches(GrindingRecipe recipe, IReadOnlyList<ItemStack?> inputs) =>
        inputs is [ItemStack input] && input.Is(recipe.Ingredient);

    private static Item ReadItem(JsonElement name) =>
        ItemsRegistry.TryGet(name.GetString()!, out var item)
            ? item
            : throw new JsonException($"There's no item {name}.");
}
