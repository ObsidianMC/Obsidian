using System.Collections;

namespace Obsidian.Nbt;

public sealed class NbtArray<T> : INbtTag, IEnumerable, ICollection where T : struct
{
    private readonly T[] array;
    public int Count => this.array.Length;

    public bool IsReadOnly => false;

    public NbtTagType Type { get; private set; }

    public string? Name { get; set; }

    public INbtTag? Parent { get; set; }

    public bool IsSynchronized => false;

    public object SyncRoot => this.array;

    public T this[int index] { get => this.array[index]; set => this.array[index] = value; }

    public NbtArray(string? name, T[] array)
    {
        (this.Name, this.array) = (name, array);

        this.Type = this.DetermingType();
    }

    public void CopyTo(Array array, int index) => this.array.CopyTo(array, index);

    public IEnumerator GetEnumerator() => this.array.GetEnumerator();

    public bool Contains(T item) => Array.IndexOf(this.array, item) >= 0;

    public T[] GetArray() => this.array;

    public override string ToString() => $"TAG_{this.Type}('{this.Name}'): {this.Count} Values";

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        var name = $"TAG_{this.Type}('{this.Name}'): {this.Count} Values";
        return name.PadLeft(name.Length + depth);
    }

    private NbtTagType DetermingType() => typeof(T) switch
    {
        Type t when t == typeof(int) => NbtTagType.IntArray,
        Type t when t == typeof(long) => NbtTagType.LongArray,
        Type t when t == typeof(byte) => NbtTagType.ByteArray,
        _ => throw new NotSupportedException($"Type {nameof(T)} is not supported.")
    };
}
