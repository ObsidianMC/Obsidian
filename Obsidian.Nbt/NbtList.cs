using System.Collections;
using System.Text;

namespace Obsidian.Nbt;

public class NbtList : INbtTag, IList<INbtTag>
{
    private readonly List<INbtTag> baseList = new();

    public int Count => baseList.Count;

    public bool IsReadOnly => false;

    public NbtTagType ListType { get; }

    public NbtTagType Type => NbtTagType.List;

    public string Name { get; set; }
    public INbtTag Parent { get; set; }

    public NbtList(NbtTagType listType, string name = "")
    {
        Name = name;
        ListType = listType;
    }

    public INbtTag this[int index] { get => baseList[index]; set => baseList[index] = value; }

    public void Add(INbtTag item)
    {
        item.Parent = this;

        baseList.Add(item);
    }

    public void Clear() => baseList.Clear();

    public bool Contains(INbtTag item) => baseList.Contains(item);

    public void CopyTo(INbtTag[] array, int arrayIndex) => baseList.CopyTo(array, arrayIndex);

    public int IndexOf(INbtTag item) => baseList.IndexOf(item);

    public void Insert(int index, INbtTag item) => baseList.Insert(index, item);

    public bool Remove(INbtTag item) => baseList.Remove(item);

    public void RemoveAt(int index) => baseList.RemoveAt(index);

    public IEnumerator<INbtTag> GetEnumerator() => baseList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var sb = new StringBuilder();
        var count = Count;

        sb.AppendLine($"TAG_List('{Name}'): {count} {(count > 1 ? "entries" : "entry")}").AppendLine("{");

        foreach (var tag in this)
            sb.AppendLine($"{tag.PrettyString()}");

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        var sb = new StringBuilder();
        var count = Count;

        var name = $"TAG_List('{Name}'): {count} {(count > 1 ? "entries" : "entry")}";

        sb.AppendLine(name.PadLeft(name.Length + depth))
            .AppendLine("{".PadLeft(depth + addBraceDepth));

        foreach (var tag in this)
        {
            var tagString = tag.PrettyString(depth + 1, addBraceDepth + 2);
            sb.AppendLine(tagString.PadLeft(tagString.Length + depth));
        }

        sb.AppendLine("}".PadLeft(depth + addBraceDepth));

        return sb.ToString();
    }
}
