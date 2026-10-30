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

    private static LootTable ReadTable(JsonElement json, string identifier)
    {
        ValidateContainerKeys(json);
        var locationChecks = Walk(json)
            .Where(value => Qualify(Text(value, "type")) == "minecraft:location_check")
            .Select(value => value.Clone())
            .ToImmutableArray();
        var references = Walk(json)
            .Where(IsTableReference)
            .Select(value => Qualify(Text(value, "value")))
            .ToImmutableArray();
        var sequence = json.TryGetProperty("random_sequence", out var name) ? name.GetString() : null;

        return new LootTable
        {
            Identifier = identifier,
            Type = Text(json, "type", "minecraft:generic"),
            RandomSequence = sequence,
            Pools = ReadArray(json, "pools", ReadPool),
            Functions = ReadFunctions(json),
            LocationChecks = locationChecks,
            References = references
        };
    }

    private static LootPool ReadPool(JsonElement json)
    {
        var bonusRolls = json.TryGetProperty("bonus_rolls", out var bonus)
            ? ReadFloatNumber(bonus)
            : new ConstantNumber { Value = 0 };

        return new LootPool
        {
            Rolls = ReadIntNumber(json.GetProperty("rolls")),
            BonusRolls = bonusRolls,
            Conditions = ReadConditions(json),
            Functions = ReadFunctions(json),
            Entries = ReadArray(json, "entries", ReadEntry)
        };
    }

    private static bool IsTableReference(JsonElement json) =>
        Qualify(Text(json, "type")) == "minecraft:loot_table"
            && json.TryGetProperty("value", out var reference)
            && reference.ValueKind == JsonValueKind.String;

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
        ValidateContainerKeys(json);
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

        if (type == "minecraft:tag")
        {
            return new TagEntry
            {
                ItemsSet = json.GetProperty("items").Clone(),
                Expanded = json.TryGetProperty("expand", out var expand) && expand.GetBoolean(),
                Weight = json.TryGetProperty("weight", out var tagWeight) ? tagWeight.GetInt32() : 1,
                Quality = json.TryGetProperty("quality", out var tagQuality) ? tagQuality.GetInt32() : 0,
                Conditions = conditions,
                Functions = ReadFunctions(json)
            };
        }

        if (type is not ("minecraft:item" or "minecraft:empty" or "minecraft:loot_table" or "minecraft:dynamic"))
            throw new InvalidDataException($"Unsupported loot entry {type}.");

        LootTable? table = null;
        string? reference = null;
        if (type == "minecraft:loot_table")
        {
            if (!json.TryGetProperty("value", out var value)
                || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object))
            {
                throw new InvalidDataException("A loot_table entry needs a table identifier or inline table value.");
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var identifier = value.GetString()!;
                if (string.IsNullOrWhiteSpace(identifier))
                    throw new InvalidDataException("A loot_table reference cannot be empty.");

                reference = Qualify(identifier);
            }
            else
            {
                table = ReadTable(value, "");
            }
        }

        if (type is "minecraft:item" or "minecraft:dynamic" && string.IsNullOrWhiteSpace(Text(json, "name")))
            throw new InvalidDataException($"A {type} entry needs a name.");

        return new DataEntry
        {
            Kind = type,
            Name = Text(json, "name"),
            Table = table,
            Reference = reference,
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

    private sealed class TagEntry : LootPoolSingletonEntry
    {
        public required JsonElement ItemsSet { get; init; }

        public bool Expanded { get; init; }

        private IEnumerable<Item> Items()
        {
            foreach (var id in RegistrySetIds(this.ItemsSet, "item"))
            {
                if (!ItemsRegistry.TryGet(id, out var item))
                    throw new InvalidDataException($"Unknown loot item {id}.");

                yield return item;
            }
        }

        public override bool Expand(LootContext context, Action<LootPoolSingletonEntry> output)
        {
            if (!ILootCondition.TestAll(this.Conditions, context))
                return false;

            if (!this.Expanded)
            {
                output(this);
                return true;
            }

            // Vanilla TagEntry expands in registry tag order before the pool makes its single weighted draw.
            foreach (var item in this.Items())
            {
                output(new ItemEntry
                {
                    Name = item,
                    Weight = this.Weight,
                    Quality = this.Quality,
                    Functions = this.Functions
                });
            }

            return true;
        }

        protected override void CreateItems(Action<ItemStack> output, LootContext context)
        {
            foreach (var item in this.Items())
                output(new ItemStack(item));
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

    private static void ValidateContainerKeys(JsonElement json)
    {
        foreach (var key in new[] { "functions", "conditions", "function" })
        {
            if (json.TryGetProperty(key, out _))
            {
                throw new InvalidDataException(
                    $"Loot key '{key}' was replaced in 26.3; use type, condition and modifier.");
            }
        }
    }

    private static INumberProvider ReadIntNumber(JsonElement json) => LootNumberProviders.ReadInt(json);

    private static INumberProvider ReadFloatNumber(JsonElement json) => LootNumberProviders.ReadFloat(json);
}
