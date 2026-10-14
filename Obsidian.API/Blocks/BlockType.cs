using Obsidian.API.Inventory;
using Obsidian.API.Plugins;

namespace Obsidian.API.Blocks;

/// <summary>
/// A block a plugin registered with <see cref="IPluginRegistry.RegisterBlock"/>, and its states.
/// </summary>
/// <remarks>
/// The server owns the block: it keeps its instance, identifier and ids, block and state, until the server stops, even
/// when the plugin reloads or unloads; a reload replaces only its properties and behaviour.
/// </remarks>
public sealed class BlockType
{
    // Each property's position in the state index: how many states apart two of its values are.
    private readonly int[] strides;

    internal BlockType(
        int id,
        int firstStateId,
        string identifier,
        IReadOnlyList<StateProperty> properties,
        IReadOnlyDictionary<string, string> defaultState)
    {
        // Like vanilla's StateDefinition, which clients build the block's states with: properties sorted by name.
        this.Properties = [.. properties.OrderBy(property => property.Name, StringComparer.Ordinal)];

        if (this.Properties.Select(property => property.Name).Distinct().Count() != this.Properties.Length)
            throw new ArgumentException($"{identifier} has a property twice.", nameof(properties));

        // Counted property by property, so that many properties can't overflow past the limit.
        var stateCount = 1;
        foreach (var property in this.Properties)
        {
            stateCount *= property.Values.Length;

            if (stateCount > MaxStates)
                throw new ArgumentException($"{identifier} has more than {MaxStates} states.", nameof(properties));
        }

        this.StateCount = stateCount;
        this.strides = Strides(this.Properties.Select(property => property.Values.Length).ToArray());

        this.Id = id;
        this.FirstStateId = firstStateId;
        this.Identifier = identifier;
        this.DefaultStateId = firstStateId + this.IndexOf(defaultState, index: 0, strict: true);
    }

    /// <summary>
    /// The most states a block may have; vanilla's block with the most has 1296.
    /// </summary>
    public const int MaxStates = 4096;

    /// <summary>
    /// The block's identifier, like <c>myplugin:ruby_block</c>.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The block's id in the block registry. Vanilla's come first; plugins' follow, in the order they were first
    /// registered.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The id of the block's first state. Its states' ids follow each other, after every state of the blocks before it.
    /// </summary>
    public int FirstStateId { get; }

    /// <summary>
    /// How many states the block has, one per combination of its properties' values.
    /// </summary>
    public int StateCount { get; }

    /// <summary>
    /// The properties the block's states vary by, sorted by name.
    /// </summary>
    public ImmutableArray<StateProperty> Properties { get; }

    /// <summary>
    /// The state the block is placed in.
    /// </summary>
    public IBlock DefaultState => new PluginBlock(this, this.DefaultStateId);

    internal int DefaultStateId { get; }

    /// <summary>
    /// The block's item, or null if it has none.
    /// </summary>
    public Item? Item { get; internal set; }

    /// <summary>
    /// The plugin that registered the block and provides its behaviour, or null while the plugin is unloaded.
    /// </summary>
    public IPluginContainer? Owner => this.Binding?.Owner;

    // The plugin and its behaviour, replaced together when the plugin registers the block again, and dropped when it
    // unloads.
    internal BlockBinding? Binding { get; set; }

    // How the block collides, lights and breaks: the server's copy of the plugin's properties, kept when the plugin
    // unloads since the block stays in the world.
    internal BlockSettings Settings { get; set; } = BlockSettings.Default;

    /// <summary>
    /// Whether <paramref name="block"/> is one of this block's states.
    /// </summary>
    public bool Is(IBlock block) => (uint)(block.GetHashCode() - this.FirstStateId) < (uint)this.StateCount;

    /// <summary>
    /// The state with the given property values; a property not given keeps the default state's value.
    /// </summary>
    /// <exception cref="ArgumentException">A property or value isn't one of the block's.</exception>
    public IBlock GetState(IReadOnlyDictionary<string, string> values) =>
        new PluginBlock(this, this.FirstStateId + this.IndexOf(values, this.DefaultStateId - this.FirstStateId, true));

    /// <summary>
    /// A property's value in one of the block's states.
    /// </summary>
    /// <exception cref="ArgumentException">The state isn't one of the block's, or the property isn't.</exception>
    public string GetValue(IBlock state, string property)
    {
        var index = this.IndexOfState(state);
        var position = this.PositionOf(property)
            ?? throw new ArgumentException($"{this.Identifier} has no property {property}.", nameof(property));

        return this.ValueAt(index, position);
    }

    /// <summary>
    /// The same state with <paramref name="property"/> set to <paramref name="value"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The state isn't one of the block's, or the property or value isn't.
    /// </exception>
    public IBlock With(IBlock state, string property, string value)
    {
        var index = this.IndexOfState(state);
        var values = new Dictionary<string, string> { [property] = value };

        return new PluginBlock(this, this.FirstStateId + this.IndexOf(values, index, strict: true));
    }

    /// <summary>
    /// Every property's value in a state, by name.
    /// </summary>
    internal IReadOnlyDictionary<string, string> ValuesOf(int stateId)
    {
        var index = stateId - this.FirstStateId;
        var values = new Dictionary<string, string>(this.Properties.Length);

        for (var position = 0; position < this.Properties.Length; position++)
            values[this.Properties[position].Name] = this.ValueAt(index, position);

        return values;
    }

    /// <summary>
    /// The state id with the given values set on <paramref name="stateId"/>, or -1 when one isn't the block's.
    /// </summary>
    internal int TryWith(int stateId, IReadOnlyDictionary<string, string> values)
    {
        var index = this.IndexOf(values, stateId - this.FirstStateId, strict: false);
        return index < 0 ? -1 : this.FirstStateId + index;
    }

    internal IBlock StateOf(int stateId) => new PluginBlock(this, stateId);

    /// <summary>
    /// The index of a state among a block's states, from the index of each property's value: the first property (by
    /// name) varies slowest and the last fastest, like vanilla numbers states.
    /// </summary>
    internal static int StateIndex(IReadOnlyList<int> valueCounts, IReadOnlyList<int> valueIndexes)
    {
        var strides = Strides(valueCounts);
        var index = 0;

        for (var position = 0; position < strides.Length; position++)
            index += valueIndexes[position] * strides[position];

        return index;
    }

    private static int[] Strides(IReadOnlyList<int> valueCounts)
    {
        var strides = new int[valueCounts.Count];
        var stride = 1;

        for (var position = valueCounts.Count - 1; position >= 0; position--)
        {
            strides[position] = stride;
            stride *= valueCounts[position];
        }

        return strides;
    }

    private int IndexOfState(IBlock state) => this.Is(state)
        ? state.GetHashCode() - this.FirstStateId
        : throw new ArgumentException($"{state} isn't a state of {this.Identifier}.", nameof(state));

    private int? PositionOf(string property)
    {
        for (var position = 0; position < this.Properties.Length; position++)
        {
            if (this.Properties[position].Name == property)
                return position;
        }

        return null;
    }

    private string ValueAt(int index, int position)
    {
        var values = this.Properties[position].Values;
        return values[index / this.strides[position] % values.Length];
    }

    // The index of the state with the given values set on the state at index, or -1 (or an exception, when strict)
    // when a property or value isn't the block's.
    private int IndexOf(IReadOnlyDictionary<string, string> values, int index, bool strict)
    {
        foreach (var (property, value) in values)
        {
            var position = this.PositionOf(property);
            var valueIndex = position is int found ? this.Properties[found].Values.IndexOf(value) : -1;

            if (position is not int at || valueIndex < 0)
            {
                return strict
                    ? throw new ArgumentException($"{property}={value} isn't a state of {this.Identifier}.")
                    : -1;
            }

            var stride = this.strides[at];
            var current = index / stride % this.Properties[at].Values.Length;
            index += (valueIndex - current) * stride;
        }

        return index;
    }

    public override string ToString() => this.Identifier;
}

/// <summary>
/// The plugin that registered a block, and what the block does: its behaviour, whether that gets random ticks, and the
/// trees it grows into when it's a sapling.
/// </summary>
internal sealed record BlockBinding(
    IPluginContainer Owner,
    IBlockBehavior? Behavior,
    TreeGrower? TreeGrower = null,
    bool RandomlyTicks = false);

/// <summary>
/// The parts of a plugin block's <see cref="BlockProperties"/> the server keeps: plain values, so that keeping them
/// doesn't keep the plugin loaded.
/// </summary>
internal sealed record BlockSettings(
    bool HasCollision,
    bool Opaque,
    float Hardness,
    float ExplosionResistance,
    bool RequiresCorrectTool,
    int LightEmission)
{
    public static BlockSettings Default { get; } = From(new BlockProperties());

    public static BlockSettings From(BlockProperties properties) => new(
        properties.HasCollision,
        properties.HasCollision && properties.Opaque,
        properties.Hardness,
        // An unbreakable block withstands explosions too, like bedrock.
        properties.ExplosionResistance ?? (properties.Hardness < 0 ? 3_600_000 : properties.Hardness),
        properties.RequiresCorrectTool,
        properties.LightEmission);
}

/// <summary>
/// A state of a plugin's block.
/// </summary>
internal readonly struct PluginBlock(BlockType type, int stateId) : IBlock, IEquatable<PluginBlock>
{
    public BlockType Type => type;

    public string UnlocalizedName => type.Identifier;

    public int BaseId => type.FirstStateId;

    public int DefaultId => type.DefaultStateId;

    public int RegistryId => type.Id;

    public IBlockState State => new PluginBlockState(stateId);

    public bool Equals(PluginBlock other) => other.GetHashCode() == stateId;

    public override bool Equals(object? obj) => obj is PluginBlock other && this.Equals(other);

    public override int GetHashCode() => stateId;

    public override string ToString() => $"{type.Identifier}#{stateId}";
}

internal sealed record PluginBlockState(int Id) : IBlockState;
