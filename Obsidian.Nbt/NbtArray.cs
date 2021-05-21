using System.Collections;
using System.Collections.Generic;

namespace Obsidian.Nbt
{
    public class NbtArray<T> : INbtTag, IEnumerable<T>, ICollection<T>
    {
        private readonly List<T> list;

        public int Count => this.list.Count;

        public bool IsReadOnly => false;

        public NbtTagType Type => NbtTagType.ByteArray;

        public string Name { get; set; }
        public INbtTag Parent { get; set; }

        public NbtArray(string name, int length) => (this.Name, this.list) = (name, new List<T>(length));

        public NbtArray(string name, IEnumerable<T> array) => (this.Name, this.list) = (name, new(array));

        public void Add(T item) => this.list.Add(item);

        public void Clear() => this.list.Clear();

        public bool Contains(T item) => this.list.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => this.list.CopyTo(array, arrayIndex);

        public bool Remove(T item) => this.list.Remove(item);

        public IEnumerator<T> GetEnumerator() => this.list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
