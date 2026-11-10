using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>Evaluates 26.3's item-supplied block transformers against the live level.</summary>
internal static partial class ItemUseOnBehaviors
{
    // Compiled from block_transformer.json on first use, once the block registry is ready.
    private static readonly Lazy<FrozenDictionary<string, CompiledTransform[]>> compiledTransformers =
        new(() => CompileTransformers(Load("block_transformer.json")));

    private static readonly ConcurrentDictionary<int, string?> defaultTransformers = new();

    /// <summary>The vanilla class declaring each overridden item <c>useOn</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> Owners = Load("item_use_on_classes.json")
        .ToFrozenDictionary(entry => entry.Key, entry => entry.Value.GetString()!);

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

    /// <summary>
    /// The stack's block transformer: its own <c>block_transformer</c> component, or its item's default, which is
    /// looked up once per item rather than for every click.
    /// </summary>
    internal static string? Transformer(ItemStack stack)
    {
        if (stack.RemoveComponents.Contains(DataComponentType.BlockTransformer))
            return null;

        var own = stack.GetComponent<SimpleDataComponent<string>>(DataComponentType.BlockTransformer);
        if (own is not null)
            return own.Value;

        return defaultTransformers.GetOrAdd(stack.Holder.Id, _ => DefaultTransformer(stack));
    }

    private static string? DefaultTransformer(ItemStack stack)
    {
        var value = CombatItems.Component(stack, "block_transformer");
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// The first of the transformer's transforms that applies to the block at <paramref name="position"/> clicked on
    /// <paramref name="face"/>, and the state it turns the block into; null when none does.
    /// </summary>
    internal static Transformation? Evaluate(
        string identifier,
        Func<Vector, IBlock> read,
        Vector position,
        BlockFace face)
    {
        if (!compiledTransformers.Value.TryGetValue(identifier, out var transforms))
            return null;

        foreach (var transform in transforms)
        {
            if (transform.DisallowedFaces.Contains(face))
                continue;

            var output = transform.Provider.Provide(read, position);
            if (output is not null)
                return new Transformation(output, transform.Data);
        }

        return null;
    }

    internal static IBlock CopyProperties(IBlock output, IBlock original)
    {
        foreach (var (name, value) in original.GetProperties())
        {
            if (output.GetProperty(name) is not null)
                output = output.WithProperty(name, value);
        }
        return output;
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
