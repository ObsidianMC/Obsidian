using System.Collections;
using System.Collections.Generic;

namespace Obsidian.Nbt
{
    public class NbtList : NbtTag, IList<NbtTag>
    {
        private readonly List<NbtTag> baseList = new();

        public int Count => this.baseList.Count;

        public bool IsReadOnly => false;

        public NbtTagType ListType { get; }

        public NbtList(NbtTagType listType) : base(NbtTagType.List)
        {
            this.ListType = listType;
        }

        public NbtTag this[int index] { get => this.baseList[index]; set => this.baseList[index] = value; }

        public void Add(NbtTag item) => this.baseList.Add(item);
        public void Clear() => this.baseList.Clear();
        public bool Contains(NbtTag item) => this.baseList.Contains(item);
        public void CopyTo(NbtTag[] array, int arrayIndex) => this.baseList.CopyTo(array, arrayIndex);
        public int IndexOf(NbtTag item) => this.baseList.IndexOf(item);
        public void Insert(int index, NbtTag item) => this.baseList.Insert(index, item);
        public bool Remove(NbtTag item) => this.baseList.Remove(item);
        public void RemoveAt(int index) => this.baseList.RemoveAt(index);
        public IEnumerator<NbtTag> GetEnumerator() => this.baseList.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
