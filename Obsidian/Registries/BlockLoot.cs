using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using System.Reflection;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.Registries;

/// <summary>Resolves both player and explosion block drops, preserving table order and component tool rules.</summary>
internal static partial class BlockLoot
{
    private static readonly Lazy<IReadOnlyDictionary<string, ImmutableArray<ToolRule>>> tools = new(ReadTools);
    private static readonly Lazy<Dictionary<string, string?>> vanillaTables = new(ReadVanillaTables);
    private static readonly ConditionalWeakTable<ILevel, RandomSequences> sequences = [];

    internal static RandomSequences SequencesFor(ILevel level) => sequences.GetValue(level,
        current => new RandomSequences(RandomState.ParseSeed(current.Seed)));

    internal static IRandomSource CreateRandom(ILevel level, IBlock block) =>
        (ResolveTable(block) ?? LootTable.Empty).CreateRandom(0, SequencesFor(level));

    public static List<ItemStack> GetDrops(
        IBlock block,
        ItemStack? tool,
        IRandomSource random,
        IEntity? breaker = null,
        float? explosionRadius = null,
        VectorD? origin = null,
        Func<JsonElement, bool>? testLocation = null,
        bool checkTool = true,
        IReadOnlyList<DataComponent>? blockEntityComponents = null,
        Action<string, Action<ItemStack>>? dynamicDrops = null)
    {
        if (checkTool && (breaker is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }
            || !HasCorrectTool(block, tool)))
            return [];

        var plugin = BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type);
        var table = ResolveTable(block);
        if (table is null)
        {
            // Only plugin blocks without an explicit table keep the legacy self-item fallback.
            return plugin && type!.Settings.LootTable is null && BlocksRegistry.TryGetItem(block, out var item)
                && (explosionRadius is not float radius || random.NextFloat() <= 1 / radius)
                ? [new ItemStack(item)] : [];
        }

        return table.GetRandomItems(new LootContext
        {
            Random = random,
            Tool = tool ?? new ItemStack(ItemsRegistry.Air, 0),
            BlockState = block,
            BlockProperties = block.GetProperties(),
            ThisEntity = breaker,
            ExplosionRadius = explosionRadius,
            Origin = origin,
            TestLocation = testLocation,
            BlockEntityComponents = blockEntityComponents ?? [],
            DynamicDrops = dynamicDrops,
            ResolveTable = PluginLootTables.Resolve
        }).Where(stack => stack.Count > 0 && stack.Holder != ItemsRegistry.Air).ToList();
    }

    private static LootTable? ResolveTable(IBlock block)
    {
        var plugin = BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type);
        var colon = block.UnlocalizedName.IndexOf(':');
        var id = plugin && type!.Settings.LootTable is string explicitTable ? LootTableJson.Qualify(explicitTable)
            : block.UnlocalizedName[..(colon + 1)] + "blocks/" + block.UnlocalizedName[(colon + 1)..];
        if (!plugin && vanillaTables.Value.TryGetValue(block.UnlocalizedName, out var vanilla))
        {
            if (vanilla is null)
                return null;

            id = vanilla;
        }

        return PluginLootTables.Resolve(id);
    }

    private static Dictionary<string, string?> ReadVanillaTables()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Obsidian.Assets.block_loot_tables.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string?>>(stream)!;
    }

    public static bool HasCorrectTool(IBlock block, ItemStack? tool)
    {
        if (!BlockPhysics.RequiresCorrectTool(block.GetHashCode()))
            return true;

        if (tool is null || tool.Count <= 0 || tool.RemoveComponents.Contains(DataComponentType.Tool))
            return false;

        var rules = tool.GetComponent<ToolDataComponent>(DataComponentType.Tool)?.Rules
            ?? tools.Value.GetValueOrDefault(tool.Holder.UnlocalizedName);
        if (rules.IsDefault)
            return false;

        // Vanilla takes the first matching rule with a correct_for_drops value. Tier exclusions precede mineable
        // tags in the generated component, so a wooden pickaxe cannot bypass incorrect_for_wooden_tool.
        foreach (var rule in rules)
        {
            if (rule.CorrectDropForBlocks is bool correct && Contains(rule.Blocks, block))
                return correct;
        }

        return false;
    }

    private static bool Contains(IdSet set, IBlock block) => set.Type == 0
        ? TagsRegistry.Contains("block", set.TagName!, block.RegistryId)
        : set.Ids?.Contains(block.RegistryId) == true;

    private static IReadOnlyDictionary<string, ImmutableArray<ToolRule>> ReadTools()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, ImmutableArray<ToolRule>>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (!item.Value.GetProperty("components").TryGetProperty("minecraft:tool", out var component))
                continue;

            result[item.Name] = component.GetProperty("rules").EnumerateArray().Select(rule => new ToolRule
            {
                Blocks = ReadSet(rule.GetProperty("blocks")),
                CorrectDropForBlocks = rule.TryGetProperty("correct_for_drops", out var correct)
                    ? correct.GetBoolean() : null
            }).ToImmutableArray();
        }

        return result;
    }

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
