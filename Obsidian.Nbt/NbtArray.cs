using System.Collections;

namespace Obsidian.Nbt;

public sealed class NbtArray<T> : INbtTag, IEnumerable, ICollection where T : struct
{
    private readonly T[] array;
    public int Count => this.array.Length;

    public bool IsReadOnly => false;

    public NbtTagType Type { get; private set; }

    public string Name { get; set; }

    public INbtTag Parent { get; set; }

    public bool IsSynchronized => this.array.IsSynchronized;

    public object SyncRoot => this.array.SyncRoot;

    public T this[int index] { get => this.array[index]; set => this.array[index] = value; }

    public NbtArray(string name, int length)
    {
        (this.Name, this.array) = (name, new T[length]);

        this.SetType();
    }

    public NbtArray(string name, IEnumerable<T> array)
    {
        (this.Name, this.array) = (name, array.ToArray());

        this.SetType();
    }

    public NbtArray(string name, T[] array)
    {
        (this.Name, this.array) = (name, array);

        this.SetType();
    }

    public void CopyTo(Array array, int index) => this.array.CopyTo(array, index);

    public IEnumerator GetEnumerator() => this.array.GetEnumerator();

    public bool Contains(T item) => this.array.Contains(item);

    public T[] GetArray() => this.array;

    public override string ToString()
    {
        switch (this.Type)
        {
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                return $"TAG_{this.Type}('{this.Name}'): {this.Count} Values";
            default:
                throw new InvalidOperationException();
        }
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        switch (this.Type)
        {
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                {
                    var name = $"TAG_{this.Type}('{this.Name}'): {this.Count} Values";
                    return name.PadLeft(name.Length + depth);
                }
            default:
                throw new InvalidOperationException();
        }
    }

    private void SetType()
    {
        if (typeof(T) == typeof(int))
        {
            this.Type = NbtTagType.IntArray;
        }
        else if (typeof(T) == typeof(long))
        {
            this.Type = NbtTagType.LongArray;
        }
        else if (typeof(T) == typeof(byte))
        {
            this.Type = NbtTagType.ByteArray;
        }
    }
}
