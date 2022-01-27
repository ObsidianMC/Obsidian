using System.Collections;

namespace Obsidian.Nbt;

public class NbtArray<T> : INbtTag, IEnumerable, ICollection
{
    private readonly T[] array;

    private NbtTagType type;

    public int Count => array.Length;

    public bool IsReadOnly => false;

    public NbtTagType Type => type;

    public string Name { get; set; }

    public INbtTag Parent { get; set; }

    public bool IsSynchronized => array.IsSynchronized;

    public object SyncRoot => array.SyncRoot;

    public T this[int index] { get => array[index]; set => array[index] = value; }

    public NbtArray(string name, int length)
    {
        (Name, array) = (name, new T[length]);

        SetType();
    }

    public NbtArray(string name, IEnumerable<T> array)
    {
        (Name, this.array) = (name, array.ToArray());

        SetType();
    }

    public NbtArray(string name, T[] array)
    {
        (Name, this.array) = (name, array);

        SetType();
    }

    public void CopyTo(Array array, int index) => this.array.CopyTo(array, index);

    public IEnumerator GetEnumerator() => array.GetEnumerator();

    public bool Contains(T item) => array.Contains(item);

    public T[] GetArray() => array;

    public override string ToString()
    {
        switch (type)
        {
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                return $"TAG_{Type}('{Name}'): {Count} Values";
            default:
                throw new InvalidOperationException();
        }
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        switch (type)
        {
            case NbtTagType.ByteArray:
            case NbtTagType.IntArray:
            case NbtTagType.LongArray:
                {
                    var name = $"TAG_{Type}('{Name}'): {Count} Values";
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
            type = NbtTagType.IntArray;
        }
        else if (typeof(T) == typeof(long))
        {
            type = NbtTagType.LongArray;
        }
        else if (typeof(T) == typeof(byte))
        {
            type = NbtTagType.ByteArray;
        }
    }
}
