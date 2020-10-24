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
                x %= 32;
                z %= 32;

                if (x < 0)
                    x = 32 + x;
                if (z < 0)
                    z = 32 + z;

                return source[x + z * Width];
            }

            set
            {
                lock (lockObject)
                {
                    x %= 32;
                    z %= 32;

                    if (x < 0)
                        x = 32 + x;
                    if (z < 0)
                        z = 32 + z;

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
