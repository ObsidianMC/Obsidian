using Obsidian.API.Inventory.DataComponents;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// Works out the numbers that 26.3's fuel, brewing fuel and compostable components hold
/// (<see cref="ComponentIntValue"/> and <see cref="ComponentFloatValue"/>). A value is a constant or the key of a
/// vanilla context int or float provider (<c>context_int_provider</c>, <c>context_float_provider</c>), worked out for
/// the block using the item, like vanilla's <c>ResolvableInt.get</c> with a block's loot context.
/// </summary>
/// <remarks>
/// Only the provider and predicate types those components' vanilla providers use are read: numbers, references,
/// <c>conditional</c>, <c>div</c>, <c>number_dispatcher</c> and <c>weighted_list</c>, with <c>match_block</c>
/// conditions. Anything else throws, since vanilla's data would have changed. This is a small stand-in for Phase 4's
/// context number providers (see <c>docs/porting-26.3-plan.md</c>), which should replace it once they land.
/// </remarks>
internal static class ComponentNumbers
{
    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> ints =
        new(() => Load("context_int_providers.json"));

    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> floats =
        new(() => Load("context_float_providers.json"));

    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> predicates =
        new(() => Load("predicates.json"));

    /// <summary>
    /// The int <paramref name="value"/> holds for <paramref name="block"/>, drawing from <paramref name="random"/>
    /// when the provider is random.
    /// </summary>
    public static int Resolve(ComponentIntValue value, IBlock block, Random random)
    {
        if (value.Provider is not string provider)
            return value.Constant;

        var context = new Context(block, random);
        return (int)Evaluate(Reference(ints.Value, provider), ints.Value, context, integer: true);
    }

    /// <summary>
    /// The float <paramref name="value"/> holds for <paramref name="block"/>.
    /// </summary>
    public static float Resolve(ComponentFloatValue value, IBlock block, Random random)
    {
        if (value.Provider is not string provider)
            return value.Constant;

        var context = new Context(block, random);
        return (float)Evaluate(Reference(floats.Value, provider), floats.Value, context, integer: false);
    }

    private static double Evaluate(
        JsonElement provider,
        IReadOnlyDictionary<string, JsonElement> registry,
        Context context,
        bool integer)
    {
        if (provider.ValueKind == JsonValueKind.Number)
            return provider.GetDouble();

        if (provider.ValueKind == JsonValueKind.String)
            return Evaluate(Reference(registry, provider.GetString()!), registry, context, integer);

        var type = provider.GetProperty("type").GetString();
        switch (type)
        {
            case "minecraft:constant":
                return provider.GetProperty("value").GetDouble();

            case "minecraft:conditional":
            {
                var holds = Test(provider.GetProperty("condition"), context);
                var branch = provider.GetProperty(holds ? "on_true" : "on_false");
                return Evaluate(branch, registry, context, integer);
            }

            case "minecraft:div":
            {
                var left = Evaluate(provider.GetProperty("left"), registry, context, integer);
                var right = Evaluate(provider.GetProperty("right"), registry, context, integer);
                if (!integer)
                    return left / right;

                // Integer division truncates, like Java's: a blast furnace burns dried kelp for 4001 / 2 = 2000 ticks.
                return right == 0 ? 0 : (int)left / (int)right;
            }

            case "minecraft:number_dispatcher":
            {
                foreach (var entry in provider.GetProperty("cases").EnumerateArray())
                {
                    if (Test(entry.GetProperty("condition"), context))
                        return Evaluate(entry.GetProperty("value"), registry, context, integer);
                }

                return Evaluate(provider.GetProperty("default"), registry, context, integer);
            }

            case "minecraft:weighted_list":
            {
                var picked = Pick(provider.GetProperty("distribution"), context.Random);
                return Evaluate(picked, registry, context, integer);
            }

            default:
                throw new NotSupportedException($"Unsupported context number provider type '{type}'.");
        }
    }

    // Vanilla's WeightedList.getRandom: one draw below the total weight, walked through the entries in order.
    private static JsonElement Pick(JsonElement distribution, Random random)
    {
        var total = 0;
        foreach (var entry in distribution.EnumerateArray())
            total += entry.GetProperty("weight").GetInt32();

        var roll = random.Next(total);
        foreach (var entry in distribution.EnumerateArray())
        {
            roll -= entry.GetProperty("weight").GetInt32();
            if (roll < 0)
                return entry.GetProperty("data");
        }

        throw new System.IO.InvalidDataException("A weighted list has no weight.");
    }

    private static bool Test(JsonElement condition, Context context)
    {
        if (condition.ValueKind == JsonValueKind.String)
            return Test(Reference(predicates.Value, condition.GetString()!), context);

        var type = condition.GetProperty("type").GetString();
        if (type != "minecraft:match_block")
            throw new NotSupportedException($"Unsupported context number predicate type '{type}'.");

        return MatchesBlocks(condition.GetProperty("blocks"), context.Block)
            && MatchesState(condition, context.Block);
    }

    private static bool MatchesBlocks(JsonElement blocks, IBlock block)
    {
        if (blocks.ValueKind == JsonValueKind.Array)
            return blocks.EnumerateArray().Any(entry => MatchesBlocks(entry, block));

        var name = blocks.GetString()!;
        if (!name.StartsWith('#'))
            return name == block.UnlocalizedName;

        return TagsRegistry.TryGet("block", name[1..], out var tag) && tag.Entries.Contains(block.RegistryId);
    }

    private static bool MatchesState(JsonElement condition, IBlock block)
    {
        if (!condition.TryGetProperty("state", out var state))
            return true;

        foreach (var property in state.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new NotSupportedException("Only exact block state values are supported.");

            if (block.GetProperty(property.Name) != property.Value.GetString())
                return false;
        }

        return true;
    }

    private static JsonElement Reference(IReadOnlyDictionary<string, JsonElement> registry, string key)
    {
        var id = key.Contains(':') ? key : "minecraft:" + key;
        return registry.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown context provider or predicate '{id}'.");
    }

    private static IReadOnlyDictionary<string, JsonElement> Load(string asset)
    {
        using var stream = typeof(ComponentNumbers).Assembly.GetManifestResourceStream($"Obsidian.Assets.{asset}")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.Clone());
    }

    private readonly record struct Context(IBlock Block, Random Random);
}
