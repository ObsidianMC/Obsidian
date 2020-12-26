using System.Collections;
using System.Collections.Generic;

namespace Obsidian.Util.Collection
{
    public class DenseCollection<T> : IEnumerable<T> where T : class
    {
        private readonly object lockObject = new object();

        private readonly T[] source;

        public int Count { get; private set; } = 0;

        public int Width { get; }

        public int Height { get; }

        public DenseCollection(int width, int height)
        {
            Width = width;
            Height = height;
            source = new T[width * height];
        }

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
}
