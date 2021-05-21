using System.Collections;
using System.Collections.Generic;

namespace Obsidian.Nbt
{
    public class NbtList : INbtTag, IList<INbtTag>
    {
        private readonly List<INbtTag> baseList = new();

        public int Count => this.baseList.Count;

        public bool IsReadOnly => false;

        public NbtTagType ListType { get; }

        public NbtTagType Type => NbtTagType.List;

        public string Name { get; set; }
        public INbtTag? Parent { get; set; }

        public NbtList(NbtTagType listType, string name = "")
        {
            this.Name = name;
            this.ListType = listType;
        }

        public INbtTag this[int index] { get => this.baseList[index]; set => this.baseList[index] = value; }

        public void Add(INbtTag item) => this.baseList.Add(item);
        public void Clear() => this.baseList.Clear();
        public bool Contains(INbtTag item) => this.baseList.Contains(item);
        public void CopyTo(INbtTag[] array, int arrayIndex) => this.baseList.CopyTo(array, arrayIndex);
        public int IndexOf(INbtTag item) => this.baseList.IndexOf(item);
        public void Insert(int index, INbtTag item) => this.baseList.Insert(index, item);
        public bool Remove(INbtTag item) => this.baseList.Remove(item);
        public void RemoveAt(int index) => this.baseList.RemoveAt(index);
        public IEnumerator<INbtTag> GetEnumerator() => this.baseList.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
