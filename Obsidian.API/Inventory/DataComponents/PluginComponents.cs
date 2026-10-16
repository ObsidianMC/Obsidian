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
    Func<INetStreamReader, TValue> Read)
{
    /// <summary>
    /// Optional persistent-codec hash for click predictions, using <see cref="ComponentHash"/>. Supply this when
    /// saved NBT loses the codec's types, such as booleans stored as bytes.
    /// It must match the client mod's HashOps value.
    /// </summary>
    public Func<TValue, int>? Hash { get; init; }
}

/// <summary>
/// A plugin's data component type, from <see cref="Plugins.IPluginRegistry.RegisterComponentType"/>: read, set and
/// remove its values on a stack with <see cref="ItemStack.TryGet"/>, <see cref="ItemStack.Set"/> and
/// <see cref="ItemStack.Remove{TValue}(PluginComponentType{TValue})"/>.
/// </summary>
public sealed class PluginComponentType<TValue>
{
    internal PluginComponentType(
        DataComponentType type,
        string identifier,
        ComponentCodec<TValue> codec,
        Func<IDisposable?> tryEnter)
    {
        this.Type = type;
        this.Identifier = identifier;
        this.Codec = codec;
        this.TryEnter = tryEnter;
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

    internal Func<IDisposable?> TryEnter { get; }

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
    /// The component as it's sent to clients, or null when it isn't sent: its type has
    /// no network codec, or its plugin is gone.
    /// </summary>
    internal PluginDataComponent? ForNetwork()
    {
        using var call = DataComponentTypesRegistry.TryEnter(this.Type, out var binding);
        return binding?.WriteNetwork is not null ? this : null;
    }

    /// <summary>Encodes under a call lease, or refuses the value once unloading starts.</summary>
    internal bool TryWriteNetwork(INetStreamWriter writer)
    {
        using var call = DataComponentTypesRegistry.TryEnter(this.Type, out var binding);
        if (binding?.WriteNetwork is not Action<INbtTag, INetStreamWriter> write)
            return false;

        write(this.ToNbt(), writer);
        return true;
    }

    /// <summary>Hashes the decoded value under its plugin's call lease when a typed hash was supplied.</summary>
    internal bool TryHash(out int hash)
    {
        using var call = DataComponentTypesRegistry.TryEnter(this.Type, out var binding);
        if (binding?.Hash is not Func<INbtTag, int> hashValue)
        {
            hash = 0;
            return false;
        }

        hash = hashValue(this.ToNbt());
        return true;
    }

    /// <summary>
    /// Writes the value with its type's network codec.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no network codec now.</exception>
    public override void Write(INetStreamWriter writer)
    {
        if (!this.TryWriteNetwork(writer))
            throw new InvalidOperationException($"{this.Identifier} isn't sent to clients.");
    }

    public bool Equals(PluginDataComponent? other) =>
        ReferenceEquals(this, other) || other is not null && this.Type == other.Type && this.key.Equals(other.key);

    public override int GetHashCode() => HashCode.Combine(this.Type, this.key);
}
