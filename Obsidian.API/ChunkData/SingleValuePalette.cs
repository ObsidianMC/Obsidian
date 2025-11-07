using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using System.Diagnostics;

namespace Obsidian.API.ChunkData;

public abstract class SingleValuePalette<T> : IPalette<T>
{
    protected bool initialized;

    public T Value { get; protected set; }

    public int Count => this.IsFull ? 1 : 0;

    public int BitCount => 0;

    public bool IsFull => this.initialized;

    public bool ShouldGrow { get; private set; }

    public abstract IPalette<T> Clone();

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

    public virtual T? GetValueFromIndex(int index) => this.Value;

    public bool TryGetId(T value, out int id)
    {
        var currentValueId = this.GetValueId(this.Value);
        var valueId = this.GetValueId(value);

        var isMatch = valueId == currentValueId;
        id = isMatch ? currentValueId : -1;

        return isMatch;
    }

    public void WriteTo(INetStreamWriter writer)
    {
        var value = this.GetValueId(this.Value);

        writer.WriteVarInt(value);
    }

    protected abstract int GetValueId(T value);
}

public sealed class SingleBlockValuePalette : SingleValuePalette<IBlock>
{
    public SingleBlockValuePalette() { }

    public SingleBlockValuePalette(IBlock value)
    {
        this.GetOrAddId(value);
    }

    public override IPalette<IBlock> Clone() => new SingleBlockValuePalette(this.Value);
    protected override int GetValueId(IBlock value) => value.GetHashCode();
}

public sealed class SingleBiomeValuePalette : SingleValuePalette<BiomeCodec>
{
    public SingleBiomeValuePalette() { }

    public SingleBiomeValuePalette(BiomeCodec value)
    {
        this.GetOrAddId(value);
    }

    public override IPalette<BiomeCodec> Clone() => new SingleBiomeValuePalette(this.Value);

    protected override int GetValueId(BiomeCodec value) => value.Id;
}
