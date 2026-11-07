using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// What an item's tool component (vanilla's <c>minecraft:tool</c>) says about a block: whether it's the right tool
/// to get the block's drops, and how fast it mines it. A stack's own component takes precedence over its item's
/// vanilla default, and a stack that removed the component isn't a tool.
/// </summary>
internal static class ItemTools
{
    private static readonly Lazy<IReadOnlyDictionary<string, VanillaTool>> vanillaTools = new(ReadTools);

    private sealed record VanillaTool(ImmutableArray<ToolRule> Rules, float DefaultMiningSpeed);

    /// <summary>
    /// Vanilla's <c>Player.hasCorrectToolForDrops</c>: a block that doesn't need a tool always drops; one that does
    /// drops only when the first rule of the tool that matches it and says so says it's correct.
    /// </summary>
    public static bool HasCorrectTool(IBlock block, ItemStack? tool)
    {
        if (!BlockPhysics.RequiresCorrectTool(block.GetHashCode()))
            return true;

        if (Tool(tool) is not VanillaTool component)
            return false;

        // Vanilla takes the first matching rule with a correct_for_drops value. Tier exclusions precede mineable
        // tags in the generated component, so a wooden pickaxe cannot bypass incorrect_for_wooden_tool.
        foreach (var rule in component.Rules)
        {
            if (rule.CorrectDropForBlocks is bool correct && Contains(rule.Blocks, block))
                return correct;
        }

        return false;
    }

    /// <summary>
    /// Vanilla's <c>Item.getDestroySpeed</c>: the speed of the first rule of the tool that matches the block and has
    /// one, else the tool's default speed, or 1 for an item that isn't a tool.
    /// </summary>
    public static float MiningSpeed(IBlock block, ItemStack? tool)
    {
        if (Tool(tool) is not VanillaTool component)
            return 1;

        foreach (var rule in component.Rules)
        {
            if (rule.Speed is float speed && Contains(rule.Blocks, block))
                return speed;
        }

        return component.DefaultMiningSpeed;
    }

    // The stack's tool component, its item's vanilla one, or null when it has none.
    private static VanillaTool? Tool(ItemStack? tool)
    {
        if (tool is null || tool.Count <= 0 || tool.RemoveComponents.Contains(DataComponentType.Tool))
            return null;

        if (tool.GetComponent<ToolDataComponent>(DataComponentType.Tool) is ToolDataComponent custom)
            return new VanillaTool(custom.Rules, custom.DefaultMiningSpeed);

        return vanillaTools.Value.GetValueOrDefault(tool.Holder.UnlocalizedName);
    }

    private static bool Contains(IdSet set, IBlock block) => set.Type == 0
        ? TagsRegistry.Contains("block", set.TagName!, block.RegistryId)
        : set.Ids?.Contains(block.RegistryId) == true;

    private static IReadOnlyDictionary<string, VanillaTool> ReadTools()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, VanillaTool>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (!item.Value.GetProperty("components").TryGetProperty("minecraft:tool", out var component))
                continue;

            var rules = component.GetProperty("rules").EnumerateArray().Select(ReadRule).ToImmutableArray();
            var defaultSpeed = component.TryGetProperty("default_mining_speed", out var speed) ? speed.GetSingle() : 1;
            result[item.Name] = new VanillaTool(rules, defaultSpeed);
        }

        return result;
    }

    private static ToolRule ReadRule(JsonElement rule) => new()
    {
        Blocks = ReadSet(rule.GetProperty("blocks")),
        Speed = rule.TryGetProperty("speed", out var speed) ? speed.GetSingle() : null,
        CorrectDropForBlocks = rule.TryGetProperty("correct_for_drops", out var correct) ? correct.GetBoolean() : null
    };

    private static IdSet ReadSet(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String && json.GetString()!.StartsWith('#'))
        {
            return new IdSet
            {
                Type = 0,
                TagName = json.GetString()![1..]
            };
        }

        var identifiers = json.ValueKind == JsonValueKind.Array
            ? json.EnumerateArray().Select(value => value.GetString()!) : [json.GetString()!];
        var ids = identifiers.Select(identifier => BlocksRegistry.TryGetDefault(identifier, out var block)
            ? block.RegistryId : -1).ToImmutableArray();
        return new IdSet
        {
            Type = ids.Length + 1,
            Ids = ids
        };
    }
}
