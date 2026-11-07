using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Entities;

/// <summary>
/// The attribute modifiers vanilla enchantments give the entity wearing or holding them (their
/// <c>minecraft:attributes</c> effects): Efficiency's mining efficiency, say, or Aqua Affinity's submerged mining
/// speed. Read from <c>enchantments.json</c>.
/// </summary>
internal static class EnchantmentAttributes
{
    private static readonly Lazy<Dictionary<string, Effect[]>> effects = new(Load);

    // One attributes effect: the attribute it modifies, how, the amount for a level, and the slots it applies in.
    private sealed record Effect(
        string Attribute,
        string Id,
        AttributeOperation Operation,
        Func<int, float> Amount,
        string[] Slots);

    /// <summary>
    /// The attribute modifiers <paramref name="item"/>'s enchantments give while it's in <paramref name="slot"/>
    /// (<c>mainhand</c>, <c>head</c>...), by attribute name, like vanilla's <c>EnchantmentHelper.forEachModifier</c>.
    /// Each modifier's id is its effect's id with the slot appended, as vanilla names it.
    /// </summary>
    public static IEnumerable<(string Attribute, EntityAttributeModifier Modifier)> Modifiers(ItemStack? item, string slot)
    {
        if (item is not { Count: > 0 })
            yield break;

        var enchantments = item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(DataComponentType.Enchantments);
        if (enchantments is null)
            yield break;

        foreach (var enchantment in enchantments.Value)
        {
            var identifier = EnchantmentsRegistry.All.FirstOrDefault(definition => definition.Id == enchantment.Id)?.Identifier;
            if (identifier is null || !effects.Value.TryGetValue(identifier, out var entries))
                continue;

            foreach (var effect in entries)
            {
                if (!effect.Slots.Any(group => CombatItems.MatchesSlot(group, slot)))
                    continue;

                var id = $"{effect.Id}/{slot}";
                var modifier = new EntityAttributeModifier(id, effect.Amount(enchantment.Level), effect.Operation);
                yield return (effect.Attribute, modifier);
            }
        }
    }

    private static Dictionary<string, Effect[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.enchantments.json")
            ?? throw new InvalidOperationException("Missing enchantments.json asset.");
        using var document = JsonDocument.Parse(stream);

        var result = new Dictionary<string, Effect[]>(StringComparer.Ordinal);
        foreach (var enchantment in document.RootElement.EnumerateObject())
        {
            if (!enchantment.Value.TryGetProperty("effects", out var all)
                || !all.TryGetProperty("minecraft:attributes", out var entries))
            {
                continue;
            }

            var slots = enchantment.Value.GetProperty("slots").EnumerateArray()
                .Select(slot => slot.GetString()!)
                .ToArray();
            result[enchantment.Name] = [.. entries.EnumerateArray().Select(entry => new Effect(
                entry.GetProperty("attribute").GetString()!,
                entry.GetProperty("id").GetString()!,
                Operation(entry.GetProperty("operation").GetString()),
                LevelBasedValue(entry.GetProperty("amount")),
                slots))];
        }

        return result;
    }

    private static AttributeOperation Operation(string? name) => name switch
    {
        "add_value" => AttributeOperation.Add,
        "add_multiplied_base" => AttributeOperation.MulBase,
        "add_multiplied_total" => AttributeOperation.MulTotal,
        _ => throw new InvalidDataException($"Unknown attribute operation {name}.")
    };

    // Vanilla's LevelBasedValue, in the forms vanilla's enchantments use for attributes, computed in single
    // precision as vanilla computes them.
    private static Func<int, float> LevelBasedValue(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var constant = json.GetSingle();
            return _ => constant;
        }

        switch (json.GetProperty("type").GetString())
        {
            case "minecraft:linear":
                var start = json.GetProperty("base").GetSingle();
                var step = json.GetProperty("per_level_above_first").GetSingle();
                return level => start + step * (level - 1);
            case "minecraft:levels_squared":
                var added = json.GetProperty("added").GetSingle();
                return level => level * level + added;
            case "minecraft:fraction":
                var numerator = LevelBasedValue(json.GetProperty("numerator"));
                var denominator = LevelBasedValue(json.GetProperty("denominator"));
                return level => Fraction(numerator(level), denominator(level));
            default:
                throw new InvalidDataException($"Unsupported level-based value {json.GetProperty("type")}.");
        }
    }

    // Vanilla's fraction is 0 when its denominator is.
    private static float Fraction(float numerator, float denominator) =>
        denominator == 0 ? 0 : numerator / denominator;
}
