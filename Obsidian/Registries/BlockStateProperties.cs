using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Generic, name-based access to block state properties (e.g. <c>waterlogged</c>, <c>distance</c>, <c>half</c>),
/// built from vanilla's blocks report (<c>Assets/blocks.json</c>).
/// </summary>
/// <remarks>
/// World generation reads and changes properties by name like vanilla does; the typed state builders are
/// better suited for hand-written code.
/// </remarks>
internal static class BlockStateProperties
{
    private static readonly Lazy<Table> table = new(Load);

    // WithProperty results by state id, property and value: generation sets the same few properties over and over, and
    // finding a state by its properties is slow.
    private static readonly ConcurrentDictionary<(int State, string Property, string Value), int> withPropertyIds = new();

    /// <summary>
    /// Gets a property value, or <c>null</c> if the block doesn't have the property.
    /// </summary>
    public static string? GetProperty(this IBlock block, string property) =>
        table.Value.Properties.TryGetValue(block.GetHashCode(), out var properties) ? properties.GetValueOrDefault(property) : null;

    internal static IReadOnlyDictionary<string, string> GetProperties(IBlock block) =>
        table.Value.Properties.GetValueOrDefault(block.GetHashCode()) ?? FrozenDictionary<string, string>.Empty;

    public static bool HasProperty(this IBlock block, string property) => block.GetProperty(property) is not null;

    /// <summary>
    /// Returns the same block with <paramref name="property"/> set, or the block unchanged if it doesn't have it.
    /// </summary>
    public static IBlock WithProperty(this IBlock block, string property, string value)
    {
        var id = withPropertyIds.GetOrAdd((block.GetHashCode(), property, value),
            static (key, block) => FindWithProperty(block, key.Property, key.Value), block);

        return id < 0 ? block : BlocksRegistry.Get(id);
    }

    public static IBlock WithProperty(this IBlock block, string property, bool value) => block.WithProperty(property, value ? "true" : "false");

    public static IBlock WithProperty(this IBlock block, string property, int value) => block.WithProperty(property, value.ToString());

    /// <summary>
    /// Gets a block state from its name and properties; unspecified properties keep their default values.
    /// </summary>
    public static IBlock GetState(string name, IReadOnlyDictionary<string, string>? properties = null)
    {
        var data = table.Value;
        if (!data.DefaultIds.TryGetValue(name, out var defaultId))
            throw new InvalidOperationException($"{name} is not a valid block.");

        if (properties is null || properties.Count == 0)
            return BlocksRegistry.Get(defaultId);

        // The properties are set one by one from the default state through the WithProperty cache, rather than finding
        // the state by its full key: a block's states cover every combination of its property values, so this reaches the
        // same state, and a property the block doesn't have or an invalid value still gives the default state.
        var stateId = defaultId;
        foreach (var (key, value) in properties)
        {
            stateId = withPropertyIds.GetOrAdd((stateId, key, value),
                static key => FindWithProperty(BlocksRegistry.Get(key.State), key.Property, key.Value));

            if (stateId < 0)
                return BlocksRegistry.Get(defaultId);
        }

        return BlocksRegistry.Get(stateId);
    }

    // The state id WithProperty returns, or -1 for the block itself.
    private static int FindWithProperty(IBlock block, string property, string value)
    {
        var current = table.Value.Properties.GetValueOrDefault(block.GetHashCode());
        if (current is null || !current.ContainsKey(property))
            return -1;

        var properties = new Dictionary<string, string>(current) { [property] = value };
        return table.Value.Ids.TryGetValue(Key(block.UnlocalizedName, properties), out var id) ? id : -1;
    }

    private static string Key(string name, IReadOnlyDictionary<string, string> properties) =>
        properties.Count == 0 ? name : $"{name}[{string.Join(',', properties.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}={entry.Value}"))}]";

    private static Table Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.blocks.json")
            ?? throw new InvalidOperationException("Missing blocks report asset.");
        using var document = JsonDocument.Parse(stream);

        var properties = new Dictionary<int, FrozenDictionary<string, string>>();
        var ids = new Dictionary<string, int>();
        var defaultIds = new Dictionary<string, int>();

        foreach (var block in document.RootElement.EnumerateObject())
        {
            foreach (var state in block.Value.GetProperty("states").EnumerateArray())
            {
                var id = state.GetProperty("id").GetInt32();
                var stateProperties = state.TryGetProperty("properties", out var values)
                    ? values.EnumerateObject().ToFrozenDictionary(value => value.Name, value => value.Value.GetString()!)
                    : FrozenDictionary<string, string>.Empty;

                properties[id] = stateProperties;
                ids[Key(block.Name, stateProperties)] = id;

                if (state.TryGetProperty("default", out var isDefault) && isDefault.GetBoolean())
                    defaultIds[block.Name] = id;
            }
        }

        return new(properties.ToFrozenDictionary(), ids.ToFrozenDictionary(), defaultIds.ToFrozenDictionary());
    }

    private sealed record Table(
        FrozenDictionary<int, FrozenDictionary<string, string>> Properties,
        FrozenDictionary<string, int> Ids,
        FrozenDictionary<string, int> DefaultIds);
}
