using Obsidian.API.Inventory;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Entries;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Loot;

/// <summary>Reads data pack block loot into the same evaluator used by container loot.</summary>
public static partial class LootTableJson
{
    /// <summary>
    /// Reads a table. Unknown entry, condition, function or number types fail at load time, rather than silently
    /// allowing drops. Registry references resolve at evaluation time so plugin items and tags remain reloadable.
    /// </summary>
    /// <exception cref="InvalidDataException">The table is malformed or uses an unsupported loot type.</exception>
    public static LootTable Parse(string json, string identifier = "")
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadTable(document.RootElement, identifier);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Invalid loot table {identifier}: {ex.Message}", ex);
        }
    }

    private static LootTable ReadTable(JsonElement json, string identifier) => new()
    {
        Identifier = identifier,
        Type = Text(json, "type", "minecraft:generic"),
        RandomSequence = json.TryGetProperty("random_sequence", out var sequence) ? sequence.GetString() : null,
        Pools = ReadArray(json, "pools", pool => new LootPool
        {
            Rolls = ReadNumber(pool.GetProperty("rolls")),
            BonusRolls = pool.TryGetProperty("bonus_rolls", out var bonus)
                ? ReadNumber(bonus) : new ConstantNumber { Value = 0 },
            Conditions = ReadConditions(pool),
            Functions = ReadFunctions(pool),
            Entries = ReadArray(pool, "entries", ReadEntry)
        }),
        Functions = ReadFunctions(json),
        LocationChecks = Walk(json).Where(value => Text(value, "condition") == "minecraft:location_check")
            .Select(value => value.Clone()).ToImmutableArray(),
        References = Walk(json).Where(value => Text(value, "type") == "minecraft:loot_table"
            && value.TryGetProperty("value", out var reference) && reference.ValueKind == JsonValueKind.String)
            .Select(value => Qualify(Text(value, "value"))).ToImmutableArray()
    };

    private static IEnumerable<JsonElement> Walk(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            yield return json;
            foreach (var property in json.EnumerateObject())
            {
                foreach (var child in Walk(property.Value))
                    yield return child;
            }
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in json.EnumerateArray())
            {
                foreach (var child in Walk(value))
                    yield return child;
            }
        }
    }

    private static ImmutableArray<T> ReadArray<T>(JsonElement json, string name, Func<JsonElement, T> read) =>
        json.TryGetProperty(name, out var array) ? array.EnumerateArray().Select(read).ToImmutableArray() : [];

    private static string Text(JsonElement json, string name, string fallback = "") =>
        json.TryGetProperty(name, out var value) ? value.GetString()! : fallback;

    internal static string Qualify(string id) => id.Contains(':') ? id : "minecraft:" + id;

    private static LootPoolEntry ReadEntry(JsonElement json)
    {
        var type = Qualify(Text(json, "type"));
        var conditions = ReadConditions(json);
        if (type is "minecraft:alternatives" or "minecraft:group" or "minecraft:sequence")
        {
            return new CompositeEntry
            {
                Kind = type,
                Conditions = conditions,
                Children = ReadArray(json, "children", ReadEntry)
            };
        }

        if (type is not ("minecraft:item" or "minecraft:empty" or "minecraft:loot_table" or "minecraft:dynamic"))
            throw new InvalidDataException($"Unsupported loot entry {type}.");

        return new DataEntry
        {
            Kind = type,
            Name = Text(json, "name"),
            Table = json.TryGetProperty("value", out var value)
                ? value.ValueKind == JsonValueKind.String ? null : ReadTable(value, "") : null,
            Reference = json.TryGetProperty("value", out value) && value.ValueKind == JsonValueKind.String
                ? Qualify(value.GetString()!) : null,
            Weight = json.TryGetProperty("weight", out var weight) ? weight.GetInt32() : 1,
            Quality = json.TryGetProperty("quality", out var quality) ? quality.GetInt32() : 0,
            Conditions = conditions,
            Functions = ReadFunctions(json)
        };
    }

    private sealed class CompositeEntry : LootPoolEntry
    {
        public required string Kind { get; init; }
        public required ImmutableArray<LootPoolEntry> Children { get; init; }

        public override bool Expand(LootContext context, Action<LootPoolSingletonEntry> output)
        {
            if (!ILootCondition.TestAll(this.Conditions, context))
                return false;

            foreach (var child in this.Children)
            {
                var expanded = child.Expand(context, output);
                if (this.Kind == "minecraft:alternatives" && expanded)
                    return true;

                if (this.Kind == "minecraft:sequence" && !expanded)
                    return false;
            }

            return this.Kind != "minecraft:alternatives";
        }
    }

    private sealed class DataEntry : LootPoolSingletonEntry
    {
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public string? Reference { get; init; }
        public LootTable? Table { get; init; }

        protected override void CreateItems(Action<ItemStack> output, LootContext context)
        {
            switch (this.Kind)
            {
                case "minecraft:item":
                    if (ItemsRegistry.TryGet(Qualify(this.Name), out var item))
                        output(new ItemStack(item));

                    break;
                case "minecraft:loot_table":
                    var table = this.Table ?? context.ResolveTable(this.Reference!);
                    table?.GetRandomItemsRaw(context, output);
                    break;
                case "minecraft:dynamic":
                    context.DynamicDrops?.Invoke(Qualify(this.Name), output);
                    break;
            }
        }
    }

    private static INumberProvider ReadNumber(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
            return new ConstantNumber { Value = json.GetSingle() };

        var type = Qualify(Text(json, "type", "minecraft:uniform"));
        return type switch
        {
            "minecraft:constant" => new ConstantNumber { Value = json.GetProperty("value").GetSingle() },
            "minecraft:uniform" => new UniformNumber
            {
                Min = ReadNumber(json.GetProperty("min")),
                Max = ReadNumber(json.GetProperty("max"))
            },
            "minecraft:binomial" => new BinomialNumber
            {
                Trials = ReadNumber(json.GetProperty("n")),
                Probability = ReadNumber(json.GetProperty("p"))
            },
            _ => throw new InvalidDataException($"Unsupported loot number {type}.")
        };
    }

    private sealed class BinomialNumber : INumberProvider
    {
        public required INumberProvider Trials { get; init; }
        public required INumberProvider Probability { get; init; }

        public int GetInt(LootContext context)
        {
            var trials = this.Trials.GetInt(context);
            var probability = this.Probability.GetFloat(context);
            var successes = 0;
            for (var i = 0; i < trials; i++)
            {
                if (context.Random.NextFloat() < probability)
                    successes++;
            }

            return successes;
        }

        public float GetFloat(LootContext context) => this.GetInt(context);
    }
}
