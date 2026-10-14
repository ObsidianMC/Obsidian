using Obsidian.API.Registries;
using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// How a plugin's component type saves its values: like vanilla's <c>DataComponentType.codec()</c>, to the NBT stacks
/// are saved with, under the type's identifier in their <c>components</c>.
/// </summary>
/// <param name="Encode">Turns a value into its saved form; the tag's name is ignored.</param>
/// <param name="Decode">Reads a value back from what <paramref name="Encode"/> wrote.</param>
public sealed record ComponentCodec<TValue>(Func<TValue, INbtTag> Encode, Func<INbtTag, TValue> Decode);

/// <summary>
/// How a plugin's component type sends its values to clients, like vanilla's <c>DataComponentType.streamCodec()</c>.
/// A client mod registering the same type must read and write the same bytes.
/// </summary>
public sealed record ComponentNetworkCodec<TValue>(
    Action<INetStreamWriter, TValue> Write,
    Func<INetStreamReader, TValue> Read);

/// <summary>
/// A plugin's data component type, from <see cref="Plugins.IPluginRegistry.RegisterComponentType"/>: read, set and
/// remove its values on a stack with <see cref="ItemStack.TryGet"/>, <see cref="ItemStack.Set"/> and
/// <see cref="ItemStack.Remove{TValue}(PluginComponentType{TValue})"/>.
/// </summary>
public sealed class PluginComponentType<TValue>
{
    internal PluginComponentType(DataComponentType type, string identifier, ComponentCodec<TValue> codec)
    {
        this.Type = type;
        this.Identifier = identifier;
        this.Codec = codec;
    }

    /// <summary>
    /// The type's id, after vanilla's: it keeps it while the server runs, across reloads of its plugin.
    /// </summary>
    public DataComponentType Type { get; }

    /// <summary>
    /// The type's identifier, in its plugin's namespace, which stacks are saved with.
    /// </summary>
    public string Identifier { get; }

    internal ComponentCodec<TValue> Codec { get; }

    public override string ToString() => this.Identifier;
}

/// <summary>
/// A plugin component on a stack, kept in its saved form, so that the stack holds nothing of the plugin's. It outlives
/// the plugin unloading, saved as it was, and its plugin reads it again once it's back.
/// </summary>
/// <remarks>
/// It's sent to clients only while its type has a network codec, which its plugin gave. Comparing two compares their
/// saved forms.
/// </remarks>
public sealed record PluginDataComponent : DataComponent
{
    // The value under the type's identifier; never handed out, so the component stays immutable.
    private readonly NbtCompound saved;
    private readonly ComponentValueKey key;

    // The network codec's writer taken as the stack was about to be sent (ForNetwork), so that the plugin unloading
    // meanwhile doesn't leave a component counted but unwritable. Not part of the value.
    private readonly Action<INbtTag, INetStreamWriter>? networkWriter;

    /// <param name="type">The component's type.</param>
    /// <param name="identifier">The type's identifier.</param>
    /// <param name="value">The saved value, which is copied.</param>
    internal PluginDataComponent(DataComponentType type, string identifier, INbtTag value)
    {
        this.Type = type;
        this.Identifier = identifier;

        // Copied through a compound; the value's own parent is put back, since the tag is the caller's.
        var parent = value.Parent;
        var wrapper = new NbtCompound();
        wrapper.Add(identifier, value);
        this.saved = ComponentCopies.Nbt(wrapper)!;
        value.Parent = parent;

        this.key = new ComponentValueKey(this.saved);
    }

    private PluginDataComponent(PluginDataComponent original, Action<INbtTag, INetStreamWriter> networkWriter)
        : base(original)
    {
        this.Type = original.Type;
        this.Identifier = original.Identifier;
        this.saved = original.saved;
        this.key = original.key;
        this.networkWriter = networkWriter;
    }

    public override DataComponentType Type { get; }

    public override string Identifier { get; }

    /// <summary>
    /// The saved value wrapped in a compound under the type's identifier, for hashing it like NBT. Don't change it.
    /// </summary>
    internal NbtCompound Saved => this.saved;

    /// <summary>
    /// A copy of the saved value, named with the type's identifier.
    /// </summary>
    internal INbtTag ToNbt()
    {
        var copy = ComponentCopies.Nbt(this.saved)!;
        var value = copy[this.Identifier];

        copy.Remove(this.Identifier);
        value.Parent = null;
        value.Name = this.Identifier;

        return value;
    }

    /// <summary>
    /// The component as it's sent to clients, with its type's network codec, or null when it isn't sent: its type has
    /// no network codec, or its plugin is gone.
    /// </summary>
    internal PluginDataComponent? ForNetwork() =>
        DataComponentTypesRegistry.NetworkWriter(this.Type) is { } write ? new(this, write) : null;

    /// <summary>
    /// Writes the value with its type's network codec.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no network codec now.</exception>
    public override void Write(INetStreamWriter writer)
    {
        var write = this.networkWriter
            ?? DataComponentTypesRegistry.NetworkWriter(this.Type)
            ?? throw new InvalidOperationException($"{this.Identifier} isn't sent to clients.");

        write(this.ToNbt(), writer);
    }

    public bool Equals(PluginDataComponent? other) =>
        ReferenceEquals(this, other) || other is not null && this.Type == other.Type && this.key.Equals(other.key);

    public override int GetHashCode() => HashCode.Combine(this.Type, this.key);
}
