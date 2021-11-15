using System.Collections;
using System.Text;

namespace Obsidian.Nbt;

public class NbtList : INbtTag, IList<INbtTag>
{
    private readonly List<INbtTag> baseList = new();

    public int Count => this.baseList.Count;

    public bool IsReadOnly => false;

    public NbtTagType ListType { get; }

    public NbtTagType Type => NbtTagType.List;

    public string Name { get; set; }
    public INbtTag Parent { get; set; }

    public NbtList(NbtTagType listType, string name = "")
    {
        this.Name = name;
        this.ListType = listType;
    }

    public INbtTag this[int index] { get => this.baseList[index]; set => this.baseList[index] = value; }

    public void Add(INbtTag item)
    {
        item.Parent = this;

        this.baseList.Add(item);
    }

    public void Clear() => this.baseList.Clear();

    public bool Contains(INbtTag item) => this.baseList.Contains(item);

    public void CopyTo(INbtTag[] array, int arrayIndex) => this.baseList.CopyTo(array, arrayIndex);

    public int IndexOf(INbtTag item) => this.baseList.IndexOf(item);

    public void Insert(int index, INbtTag item) => this.baseList.Insert(index, item);

    public bool Remove(INbtTag item) => this.baseList.Remove(item);

    public void RemoveAt(int index) => this.baseList.RemoveAt(index);

    public IEnumerator<INbtTag> GetEnumerator() => this.baseList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public string PrettyString(int depth = 4)
    {
        var sb = new StringBuilder();
        var count = this.Count;

        var t = $"TAG_List('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}";

        sb.AppendLine(t.PadLeft(depth + t.Length))
            .AppendLine("{".PadLeft(depth * 2 + 1));

        foreach (var tag in this)
        {
            var pretty = tag.PrettyString(depth);
            sb.AppendLine($"{pretty}".PadLeft(pretty.Length + depth * 2));
        }

        sb.AppendLine("}".PadLeft(depth * 2 + 1));

        return sb.ToString();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var count = this.Count;

        sb.AppendLine($"TAG_List('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}").AppendLine("{");

        foreach (var tag in this)
            sb.AppendLine($"{tag.PrettyString()}");

        sb.AppendLine("}");

        return sb.ToString();
    }
}
