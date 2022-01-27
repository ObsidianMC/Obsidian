using System.Collections;
using System.Text;

namespace Obsidian.Nbt;

public class NbtCompound : INbtTag, IEnumerable<KeyValuePair<string, INbtTag>>
{
    private readonly Dictionary<string, INbtTag> children = new();

    public int Count => children.Count;

    public NbtTagType Type => NbtTagType.Compound;

    public string Name { get; set; }

    public INbtTag Parent { get; set; }

    public INbtTag this[string name] { get => children[name]; set => Add(name, value); }

    public NbtCompound(string name = "")
    {
        if (Parent?.Type == NbtTagType.Compound && string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags within a compound must be named.");

        Name = name;
    }

    public NbtCompound(List<INbtTag> children) : this()
    {
        foreach (var child in children)
            Add(child.Name, child);
    }

    public NbtCompound(string name, List<INbtTag> children) : this(name)
    {
        foreach (var child in children)
            Add(child.Name, child);
    }

    public bool Remove(string name) => children.Remove(name);

    public bool HasTag(string name) => children.ContainsKey(name);

    public bool TryGetTag(string name, out INbtTag tag) => children.TryGetValue(name, out tag);

    private T GetTagValue<T>(string name)
    {
        if (TryGetTag(name, out var tag))
        {
            var actualTag = (NbtTag<T>)tag;

            return actualTag.Value;
        }

        return default;
    }

    public byte GetByte(string name) => GetTagValue<byte>(name);

    public short GetShort(string name) => GetTagValue<short>(name);

    public int GetInt(string name) => GetTagValue<int>(name);

    public long GetLong(string name) => GetTagValue<long>(name);

    public float GetFloat(string name) => GetTagValue<float>(name);

    public double GetDouble(string name) => GetTagValue<double>(name);

    public string GetString(string name) => GetTagValue<string>(name);

    public bool GetBool(string name)
    {
        if (!TryGetTag(name, out var tag))
            return false;

        var actualTag = (NbtTag<byte>)tag;

        return actualTag.Value == 1;
    }

    public void Clear() => children.Clear();

    public override string ToString()
    {
        var sb = new StringBuilder();
        var count = Count;

        sb.AppendLine($"TAG_Compound('{Name}'): {count} {(count > 1 ? "entries" : "entry")}")
            .AppendLine("{");

        foreach (var (_, tag) in this)
            sb.AppendLine(tag.PrettyString());

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        var sb = new StringBuilder();
        var count = Count;

        var name = $"TAG_Compound('{Name}'): {count} {(count > 1 ? "entries" : "entry")}";

        sb.AppendLine(name.PadLeft(name.Length + depth))
            .AppendLine("{".PadLeft(depth + addBraceDepth));

        foreach (var (_, tag) in this)
        {
            var tagString = tag.PrettyString(depth + 1, addBraceDepth + 2);
            sb.AppendLine(tagString.PadLeft(tagString.Length + depth));
        }

        sb.AppendLine("}".PadLeft(depth + addBraceDepth));

        return sb.ToString();
    }

    public void Add(string name, INbtTag tag)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags inside a compound must be named.");

        tag.Parent = this;

        children.Add(name, tag);
    }

    public void Add(INbtTag tag)
    {
        if (string.IsNullOrEmpty(tag.Name))
            throw new InvalidOperationException("Tags inside a compound must be named.");

        tag.Parent = this;

        children.Add(tag.Name, tag);
    }

    public IEnumerator<KeyValuePair<string, INbtTag>> GetEnumerator() => children.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
