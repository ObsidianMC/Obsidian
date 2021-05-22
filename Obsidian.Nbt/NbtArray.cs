using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Nbt
{
    public class NbtArray<T> : INbtTag, IEnumerable, ICollection
    {
        private readonly T[] array;

        public int Count => this.array.Length;

        public bool IsReadOnly => false;

        public NbtTagType Type => NbtTagType.ByteArray;

        public string Name { get; set; }
        public INbtTag Parent { get; set; }

        public bool IsSynchronized => this.array.IsSynchronized;

        public object SyncRoot => this.array.SyncRoot;

        public T this[int index] { get => this.array[index]; set => this.array[index] = value; }

        public NbtArray(string name, int length) => (this.Name, this.array) = (name, new T[length]);

        public NbtArray(string name, IEnumerable<T> array) => (this.Name, this.array) = (name, array.ToArray());

        public NbtArray(string name, T[] array) => (this.Name, this.array) = (name, array);

        public void CopyTo(Array array, int index) => this.array.CopyTo(array, index);
        public IEnumerator GetEnumerator() => this.array.GetEnumerator();

        public bool Contains(T item) => this.array.Contains(item);
        public T[] GetArray() => this.array;
       
    }
}
