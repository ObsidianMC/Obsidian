using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// The <c>minecraft:tool</c> component each item has by default, from vanilla 1.21.11's item components
/// (<c>Assets/item_components.json</c>). Loaded once; block tags in the rules are resolved to block ids at load.
/// </summary>
internal static class ItemToolDefaults
{
    private static readonly Lazy<FrozenDictionary<int, ToolDataComponent>> defaults = new(Load);

    /// <summary>
    /// The item's default tool component, or null for items without one.
    /// </summary>
    public static ToolDataComponent? Get(int itemId) => defaults.Value.GetValueOrDefault(itemId);

    private static FrozenDictionary<int, ToolDataComponent> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.item_components.json")
            ?? throw new InvalidOperationException("Obsidian.Assets.item_components.json is missing; run Obsidian.AssetGenerator.");
        using var document = JsonDocument.Parse(stream);

        var result = new Dictionary<int, ToolDataComponent>();
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (!item.Value.GetProperty("components").TryGetProperty("minecraft:tool", out var tool))
                continue;

            var rules = tool.GetProperty("rules").EnumerateArray().Select(ReadRule).ToImmutableArray();
            var speed = tool.TryGetProperty("default_mining_speed", out var defaultSpeed) ? defaultSpeed.GetSingle() : 1f;
            var damage = tool.TryGetProperty("damage_per_block", out var perBlock) ? perBlock.GetInt32() : 1;
            var creative = !tool.TryGetProperty("can_destroy_blocks_in_creative", out var inCreative) || inCreative.GetBoolean();
            result[ItemsRegistry.Get(item.Name).Id] = ToolDataComponent.Create(rules, speed, damage, creative);
        }

        return result.ToFrozenDictionary();
    }

    private static ToolRule ReadRule(JsonElement rule) => new()
    {
        Blocks = ReadIdSet(rule.GetProperty("blocks")),
        Speed = rule.TryGetProperty("speed", out var speed) ? speed.GetSingle() : null,
        CorrectDropForBlocks = rule.TryGetProperty("correct_for_drops", out var correct) ? correct.GetBoolean() : null,
    };

    private static IdSet ReadIdSet(JsonElement blocks)
    {
        if (blocks.ValueKind == JsonValueKind.Array)
        {
            // Inline lists of block ids.
            var ids = blocks.EnumerateArray().Select(block => BlocksRegistry.Get(block.GetString()!).RegistryId).ToImmutableArray();
            return new IdSet { Type = ids.Length + 1, Ids = ids };
        }

        var name = blocks.GetString()!;
        if (!name.StartsWith('#'))
        {
            var block = BlocksRegistry.Get(name);
            return new IdSet { Type = 2, Ids = [block.RegistryId] };
        }

        // A tag: resolve it now, so checks compare ids instead of looking the tag up.
        var tag = BlockTags.Find(name) ?? throw new InvalidOperationException($"Unknown block tag {name} in the tool defaults.");
        return new IdSet { Type = tag.Entries.Length + 1, Ids = tag.Entries };
    }
}
