using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>Evaluates 26.3's item-supplied block transformers against the live level.</summary>
internal static partial class ItemUseOnBehaviors
{
    private static readonly FrozenDictionary<string, JsonElement> transformers = Load("block_transformer.json");

    internal sealed record Transformation(IBlock State, JsonElement Data)
    {
        public string Sound => Data.GetProperty("sound").GetString()!;

        public int Damage => Data.TryGetProperty("item_damage_per_use", out var damage) ? damage.GetInt32() : 0;

        public string? Loot => Data.TryGetProperty("loot", out var value) ? value.GetString() : null;
    }

    internal static FrozenDictionary<string, JsonElement> Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.{name}")
            ?? throw new InvalidOperationException($"Missing {name} asset.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject()
            .ToFrozenDictionary(entry => entry.Name, entry => entry.Value.Clone());
    }

    internal static string? Component(ItemStack stack, DataComponentType type, string name)
    {
        if (stack.RemoveComponents.Contains(type))
            return null;

        if (stack.GetComponent<SimpleDataComponent<string>>(type) is SimpleDataComponent<string> own)
            return own.Value;

        var value = CombatItems.Component(stack, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    internal static Transformation? Evaluate(
        string identifier,
        Func<Vector, IBlock> read,
        Vector position,
        BlockFace face)
    {
        if (!transformers.TryGetValue(identifier, out var transforms))
            return null;

        var faceName = face.ToString().ToLowerInvariant();
        foreach (var transform in transforms.EnumerateArray())
        {
            if (transform.TryGetProperty("disallowed_faces", out var faces)
                && faces.EnumerateArray().Any(value => value.GetString() == faceName))
            {
                continue;
            }

            var provider = transform.GetProperty("block_state_provider");
            var output = Provide(provider, read, position);
            if (output is not null)
                return new Transformation(output, transform);
        }

        return null;
    }

    // Matches the provider types used by vanilla 26.3's transformers; no legacy worldgen tables are consulted.
    private static IBlock? Provide(JsonElement provider, Func<Vector, IBlock> read, Vector position)
    {
        if (!provider.TryGetProperty("type", out var type))
            return ReadState(provider);

        switch (type.GetString())
        {
            case "minecraft:rule_based":
                foreach (var rule in provider.GetProperty("rules").EnumerateArray())
                {
                    var predicate = rule.GetProperty("if_true");
                    if (Matches(predicate, read, position))
                    {
                        var result = rule.GetProperty("then");
                        return Provide(result, read, position);
                    }
                }
                return null;

            case "minecraft:copy_properties":
                var source = provider.GetProperty("source");
                var state = ReadState(source);
                var original = read(position);
                return CopyProperties(state, original);

            default:
                throw new NotSupportedException($"Unsupported transformer provider {type.GetString()}.");
        }
    }

    private static IBlock CopyProperties(IBlock output, IBlock original)
    {
        foreach (var (name, value) in original.GetProperties())
        {
            if (output.GetProperty(name) is not null)
                output = output.WithProperty(name, value);
        }
        return output;
    }

    private static bool Matches(JsonElement predicate, Func<Vector, IBlock> read, Vector position)
    {
        if (predicate.TryGetProperty("offset", out var offset))
        {
            var coordinates = offset.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            position += new Vector(coordinates[0], coordinates[1], coordinates[2]);
        }

        switch (predicate.GetProperty("type").GetString())
        {
            case "minecraft:all_of":
                foreach (var child in predicate.GetProperty("predicates").EnumerateArray())
                {
                    if (!Matches(child, read, position))
                        return false;
                }
                return true;

            case "minecraft:matching_blocks":
                var blocks = predicate.GetProperty("blocks");
                var identifier = read(position).UnlocalizedName;
                if (blocks.ValueKind == JsonValueKind.String)
                    return blocks.GetString() == identifier;

                return blocks.EnumerateArray().Any(block => block.GetString() == identifier);

            case "minecraft:matching_block_tag":
                var name = predicate.GetProperty("tag").GetString()!;
                return TagsRegistry.TryGet("block", name, out var tag)
                    && tag.Entries.Contains(read(position).RegistryId);

            default:
                throw new NotSupportedException("Unsupported transformer block predicate.");
        }
    }

    internal static IBlock ReadState(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var identifier = value.GetString()!;
            return BlocksRegistry.Get(identifier);
        }

        var name = value.GetProperty("id").GetString()!;
        var block = BlocksRegistry.Get(name);
        if (value.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
                block = block.WithProperty(property.Name, property.Value.GetString()!);
        }
        return block;
    }
}
