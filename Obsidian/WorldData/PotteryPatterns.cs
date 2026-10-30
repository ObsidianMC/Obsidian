using Obsidian.API.Inventory;
using System.Collections.Frozen;

namespace Obsidian.WorldData;

/// <summary>Resolves a decorated pot face from its stack's component, including overrides and removals.</summary>
public static class PotteryPatterns
{
    private static readonly FrozenDictionary<string, string> assets = ItemUseOnBehaviors
        .Load("decorated_pot_pattern.json")
        .ToFrozenDictionary(entry => entry.Key, entry => entry.Value.GetProperty("asset_id").GetString()!);

    public static string? Pattern(ItemStack stack) => ItemUseOnBehaviors.Component(
        stack,
        DataComponentType.ProvidesPotteryPattern,
        "provides_pottery_pattern");

    public static string? Asset(ItemStack stack)
    {
        var pattern = Pattern(stack);
        return pattern is null ? null : assets.GetValueOrDefault(pattern);
    }
}
