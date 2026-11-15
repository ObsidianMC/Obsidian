using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using System.Diagnostics;

namespace Obsidian.API.ChunkData;

/// <summary>
/// Represents a section containing at most one distinct value without per-position storage.
/// </summary>
public abstract class SingleValuePalette<T> : IPalette<T>
{
    /// <summary>
    /// Records whether the palette has received its first value.
    /// </summary>
    protected bool initialized;

    /// <summary>
    /// Gets or sets the sole value represented by the palette.
    /// </summary>
    public T Value { get; protected set; }

    /// <summary>
    /// Gets one when initialized, otherwise zero.
    /// </summary>
    public int Count => this.IsFull ? 1 : 0;

    /// <summary>
    /// Gets zero because no per-position index bits are stored.
    /// </summary>
    public int BitCount => 0;

    /// <summary>
    /// Gets whether the palette has been initialized with a value.
    /// </summary>
    public bool IsFull => this.initialized;

    /// <summary>
    /// Gets whether an attempt to add a different value requires a larger palette.
    /// </summary>
    public bool ShouldGrow { get; private set; }

    /// <summary>
    /// Creates a copy of this single-value palette.
    /// </summary>
    public abstract IPalette<T> Clone();

    /// <summary>
    /// Initializes the sole value or flags growth for a different value, returning the value's registry identifier.
    /// </summary>
    public int GetOrAddId(T value)
    {
        var valueId = this.GetValueId(value);
        if (!this.initialized)
        {
            this.initialized = true;
            this.Value = value;

            return valueId;
        }

        if (this.TryGetId(value, out var id))
            return id;

        this.ShouldGrow = true;
        //Returns -1 signifying that the palette needs to grow.
        return valueId;
    }

    /// <summary>
    /// Returns the sole value regardless of the supplied index.
    /// </summary>
    public virtual T? GetValueFromIndex(int index) => this.Value;

    /// <summary>
    /// Compares registry identifiers and returns the current identifier on a match, otherwise -1.
    /// </summary>
    public bool TryGetId(T value, out int id)
    {
        var currentValueId = this.GetValueId(this.Value);
        var valueId = this.GetValueId(value);

        var isMatch = valueId == currentValueId;
        id = isMatch ? currentValueId : -1;

        return isMatch;
    }

    /// <summary>
    /// Writes the sole value's registry identifier as a variable-length integer.
    /// </summary>
    public void WriteTo(INetStreamWriter writer)
    {
        var value = this.GetValueId(this.Value);

        writer.WriteVarInt(value);
    }

    /// <summary>
    /// Returns the registry identifier used to compare and serialize this value.
    /// </summary>
    protected abstract int GetValueId(T value);
}

/// <summary>
/// Represents a block section filled with one state, identified by its block state ID.
/// </summary>
public sealed class SingleBlockValuePalette : SingleValuePalette<IBlock>
{
    /// <summary>
    /// Creates a block palette without an initialized value.
    /// </summary>
    public SingleBlockValuePalette() { }

    /// <summary>
    /// Creates a block palette initialized with the supplied state.
    /// </summary>
    public SingleBlockValuePalette(IBlock value)
    {
        this.GetOrAddId(value);
    }

    /// <summary>
    /// Creates a block palette initialized with the current value.
    /// </summary>
    public override IPalette<IBlock> Clone() => new SingleBlockValuePalette(this.Value);
    /// <summary>
    /// Returns the block state ID used to compare and serialize the value.
    /// </summary>
    protected override int GetValueId(IBlock value) => value.GetHashCode();
}

/// <summary>
/// Represents a biome section filled with one biome, identified by its registry ID.
/// </summary>
public sealed class SingleBiomeValuePalette : SingleValuePalette<BiomeCodec>
{
    /// <summary>
    /// Creates a biome palette without an initialized value.
    /// </summary>
    public SingleBiomeValuePalette() { }

    /// <summary>
    /// Creates a biome palette initialized with the supplied biome.
    /// </summary>
    public SingleBiomeValuePalette(BiomeCodec value)
    {
        this.GetOrAddId(value);
    }

    // A palette nothing was written to yet (a section whose biomes were never filled) clones empty.
    /// <summary>
    /// Creates a biome palette copy, preserving an uninitialized palette as empty.
    /// </summary>
    public override IPalette<BiomeCodec> Clone() =>
        this.initialized ? new SingleBiomeValuePalette(this.Value) : new SingleBiomeValuePalette();

    /// <summary>
    /// Returns the biome registry ID used to compare and serialize the value.
    /// </summary>
    protected override int GetValueId(BiomeCodec value) => value.Id;
}
