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

    /// <summary>The currently loaded plugin's sound type, or stone when the plugin is unloaded.</summary>
    public BlockSoundProperties SoundType => this.Binding?.SoundType ?? BlockSoundProperties.Stone;

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
    bool RandomlyTicks = false,
    BlockSoundProperties? SoundType = null);

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
    int LightEmission,
    string? LootTable)
{
    public static BlockSettings Default { get; } = From(new BlockProperties());

    /// <summary>
    /// Each state's light level, by its index among the block's states, when
    /// <see cref="BlockProperties.StateLightEmission"/> gave them; null when every state gives
    /// <see cref="LightEmission"/>.
    /// </summary>
    public ImmutableArray<byte>? StateLightEmission { get; init; }

    /// <summary>
    /// The light level of the block's state at <paramref name="stateIndex"/> among its states.
    /// </summary>
    public int LightEmissionOf(int stateIndex) =>
        this.StateLightEmission is ImmutableArray<byte> levels ? levels[stateIndex] : this.LightEmission;

    /// <summary>
    /// Each state's own shapes, by its index among the block's states, when <see cref="BlockProperties.StateShapes"/>
    /// gave them (null for a state it gave none); null when no state has shapes of its own.
    /// </summary>
    public ImmutableArray<PluginStateShapes?>? StateShapes { get; init; }

    /// <summary>
    /// The shapes of the block's state at <paramref name="stateIndex"/> among its states, or null for the usual
    /// shapes of its template state.
    /// </summary>
    public PluginStateShapes? ShapesOf(int stateIndex) =>
        this.StateShapes is ImmutableArray<PluginStateShapes?> shapes ? shapes[stateIndex] : null;

    /// <summary>
    /// The settings of <paramref name="properties"/>, asking <see cref="BlockProperties.StateLightEmission"/> and
    /// <see cref="BlockProperties.StateShapes"/> for each state of <paramref name="type"/> when both are given.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A state's light level isn't from 0 to 15.</exception>
    /// <exception cref="ArgumentException">A state of a block without collision has collision boxes.</exception>
    public static BlockSettings From(BlockProperties properties, BlockType? type = null) => new(
        properties.HasCollision,
        properties.HasCollision && properties.Opaque,
        properties.Hardness,
        // An unbreakable block withstands explosions too, like bedrock.
        properties.ExplosionResistance ?? (properties.Hardness < 0 ? 3_600_000 : properties.Hardness),
        properties.RequiresCorrectTool,
        properties.LightEmission,
        properties.LootTable)
    {
        StateLightEmission = type is not null && properties.StateLightEmission is { } emission
            ? LevelsOf(type, emission)
            : null,
        StateShapes = type is not null && properties.StateShapes is { } shapes
            ? ShapesOf(type, shapes, properties.HasCollision)
            : null
    };

    private static ImmutableArray<PluginStateShapes?> ShapesOf(
        BlockType type,
        Func<IReadOnlyDictionary<string, string>, BlockShapes?> shapesOf,
        bool hasCollision)
    {
        var shapes = new PluginStateShapes?[type.StateCount];

        for (var index = 0; index < shapes.Length; index++)
        {
            if (shapesOf(type.ValuesOf(type.FirstStateId + index)) is not BlockShapes given)
                continue;

            // Light and heightmaps take a block without collision for one entities pass through.
            if (!hasCollision && given.Collision.Count > 0)
            {
                throw new ArgumentException(
                    $"{type.Identifier} has no collision, so its states can't have collision boxes.",
                    nameof(BlockProperties.StateShapes));
            }

            shapes[index] = new PluginStateShapes(given);
        }

        return [.. shapes];
    }

    private static ImmutableArray<byte> LevelsOf(
        BlockType type,
        Func<IReadOnlyDictionary<string, string>, int> emission)
    {
        var levels = new byte[type.StateCount];

        for (var index = 0; index < levels.Length; index++)
        {
            var level = emission(type.ValuesOf(type.FirstStateId + index));
            ArgumentOutOfRangeException.ThrowIfNegative(level, nameof(BlockProperties.StateLightEmission));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 15, nameof(BlockProperties.StateLightEmission));

            levels[index] = (byte)level;
        }

        return [.. levels];
    }
}

/// <summary>
/// A plugin block state's own shapes, from <see cref="BlockProperties.StateShapes"/>, and their boxes as the
/// <c>{ minX, minY, minZ, maxX, maxY, maxZ }</c> arrays vanilla's captured shapes are read as, made once here so that
/// collision doesn't make them on every look.
/// </summary>
internal sealed class PluginStateShapes(BlockShapes shapes)
{
    /// <summary>The shapes the plugin gave, which copied and checked their boxes.</summary>
    public BlockShapes Shapes { get; } = shapes;

    /// <summary>The collision boxes as arrays.</summary>
    public IReadOnlyList<double[]> CollisionBoxes { get; } = Boxes(shapes.Collision);

    /// <summary>The outline boxes as arrays.</summary>
    public IReadOnlyList<double[]> OutlineBoxes { get; } = Boxes(shapes.Outline);

    private static double[][] Boxes(IReadOnlyList<BoundingBox> boxes) =>
    [
        .. boxes.Select(box => new[] { box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z })
    ];
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
