using System.Collections;

namespace Obsidian.Utilities.Collections;

public sealed class DenseCollection<T>(int width, int height) : IEnumerable<T?>
{
    private readonly object lockObject = new();

    private readonly T[] source = new T[width * height];

    public int Count { get; private set; } = 0;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public T this[int x, int z]
    {
        get
        {
            x %= Width;
            z %= Width;

            if (x < 0)
                x = Width + x;
            if (z < 0)
                z = Width + z;

            return source[x + z * Width];
        }

        set
        {
            lock (lockObject)
            {
                x %= Width;
                z %= Width;

                if (x < 0)
                    x = Width + x;
                if (z < 0)
                    z = Width + z;

                int index = x + z * Width;

                if (value != null && source[index] == null)
                    Count++;
                else if (value == null && source[index] != null)
                    Count--;

                source[index] = value;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                yield return source[i];
        }
    }
}
