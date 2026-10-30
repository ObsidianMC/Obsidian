using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// The 26.3 item components that make an item fuel or compost: <c>minecraft:cooking_fuel</c>,
/// <c>minecraft:brewing_fuel</c> and <c>minecraft:compostable</c>. A stack has its own when it sets one, none when it
/// removes it, and its item's default otherwise (from <c>item_components.json</c>).
/// </summary>
/// <remarks>Work out their numbers with <see cref="ComponentNumbers"/>, for the block using the item.</remarks>
internal static class ItemFuels
{
    private static readonly Lazy<Defaults> defaults = new(LoadDefaults);

    /// <summary>
    /// The stack's <c>minecraft:cooking_fuel</c>, which furnaces burn: its burn time and cooking speed.
    /// </summary>
    public static FuelComponentData? CookingFuel(ItemStack item) =>
        Get(item, DataComponentType.CookingFuel, defaults.Value.Cooking);

    /// <summary>
    /// The stack's <c>minecraft:brewing_fuel</c>, which brewing stands burn: its brews and brewing speed.
    /// </summary>
    public static FuelComponentData? BrewingFuel(ItemStack item) =>
        Get(item, DataComponentType.BrewingFuel, defaults.Value.Brewing);

    /// <summary>
    /// The stack's <c>minecraft:compostable</c>: the layers it adds to a composter.
    /// </summary>
    public static ComponentIntValue? Compostable(ItemStack item)
    {
        if (item.IsAir || item.RemoveComponents.Contains(DataComponentType.Compostable))
            return null;

        var own = item.GetComponent<SimpleDataComponent<ComponentIntValue>>(DataComponentType.Compostable);
        if (own is not null)
            return own.Value;

        return defaults.Value.Compostable.TryGetValue(item.Holder.UnlocalizedName, out var layers) ? layers : null;
    }

    private static FuelComponentData? Get(
        ItemStack item,
        DataComponentType type,
        FrozenDictionary<string, FuelComponentData> itemDefaults)
    {
        if (item.IsAir || item.RemoveComponents.Contains(type))
            return null;

        var own = item.GetComponent<SimpleDataComponent<FuelComponentData>>(type);
        if (own is not null)
            return own.Value;

        return itemDefaults.GetValueOrDefault(item.Holder.UnlocalizedName);
    }

    private static Defaults LoadDefaults()
    {
        var assembly = typeof(ItemFuels).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);

        var cooking = new Dictionary<string, FuelComponentData>();
        var brewing = new Dictionary<string, FuelComponentData>();
        var compostable = new Dictionary<string, ComponentIntValue>();

        foreach (var item in document.RootElement.EnumerateObject())
        {
            var components = item.Value.GetProperty("components");

            if (components.TryGetProperty("minecraft:cooking_fuel", out var cookingFuel))
                cooking.Add(item.Name, ReadFuel(cookingFuel, "burn_time"));

            if (components.TryGetProperty("minecraft:brewing_fuel", out var brewingFuel))
                brewing.Add(item.Name, ReadFuel(brewingFuel, "uses"));

            if (components.TryGetProperty("minecraft:compostable", out var compost))
                compostable.Add(item.Name, ReadInt(compost.GetProperty("layers")));
        }

        return new Defaults(
            cooking.ToFrozenDictionary(),
            brewing.ToFrozenDictionary(),
            compostable.ToFrozenDictionary());
    }

    // Vanilla's codecs give a missing amount nothing and a missing speed multiplier 1.
    private static FuelComponentData ReadFuel(JsonElement fuel, string amount)
    {
        var amountValue = fuel.TryGetProperty(amount, out var amountJson)
            ? ReadInt(amountJson)
            : new ComponentIntValue(0);
        var speed = fuel.TryGetProperty("speed_multiplier", out var speedJson)
            ? ReadFloat(speedJson)
            : new ComponentFloatValue(1f);

        return new FuelComponentData(amountValue, speed);
    }

    private static ComponentIntValue ReadInt(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? new ComponentIntValue(value.GetInt32())
        : new ComponentIntValue(0, value.GetString());

    private static ComponentFloatValue ReadFloat(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? new ComponentFloatValue(value.GetSingle())
        : new ComponentFloatValue(0, value.GetString());

    private sealed record Defaults(
        FrozenDictionary<string, FuelComponentData> Cooking,
        FrozenDictionary<string, FuelComponentData> Brewing,
        FrozenDictionary<string, ComponentIntValue> Compostable);
}
