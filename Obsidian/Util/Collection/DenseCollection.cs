using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Util.Collection
{
    public class DenseCollection<T> : IEnumerable<T>
    {
        private object lockObject = new object();

        private T[] source;

        public int Count => this.source.Length;

        public int Width { get; }

        public int Height { get; }

        public DenseCollection(int width, int height)
        {
            this.Width = width;
            this.Height = height;
            this.source = new T[width * height];
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

                return this.source[x + z * this.Width];
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

                    this.source[x + z * this.Width] = value;
                }
            }
        }

        public void Add(int x, int z, T item)
        {
            lock (lockObject) this[x, z] = item;
        }

        public void Remove(int x, int z)
        {
            var removeAt = x + z * this.Width;
            lock (lockObject) this.source = this.source.Where((s, index) => index != removeAt).ToArray();
        }

        public IEnumerator GetEnumerator() => this.source.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => (IEnumerator<T>)this.source.ToArray().GetEnumerator();
    }
}
