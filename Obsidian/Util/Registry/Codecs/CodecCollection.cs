using System.Collections;
using System.Collections.Generic;

namespace Obsidian.Util.Registry.Codecs
{
    public class CodecCollection<T> : IList<T>
    {
        private readonly List<T> source;

        public string Name { get; set; }

        public int Count => this.source.Count;

        public bool IsReadOnly => false;

        public T this[int index] { get => this.source[index]; set => this.source[index] = value; }

        public CodecCollection()
        {
            this.source = new List<T>();
        }

        public void Add(T item) => this.source.Add(item);

        public void AddRange(IEnumerable<T> current) => this.source.AddRange(current);

        public bool Remove(T item) => this.source.Remove(item);

        public int IndexOf(T item) => this.source.IndexOf(item);

        public void Insert(int index, T item) => this.source.Insert(index, item);

        public void RemoveAt(int index) => this.source.RemoveAt(index);

        public void Clear() => this.source.Clear();

        public bool Contains(T item) => this.source.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => this.source.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => this.source.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
