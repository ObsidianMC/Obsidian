using System.Collections;
using System.Text;

namespace Obsidian.Nbt;

public class NbtCompound : INbtTag, IEnumerable<KeyValuePair<string, INbtTag>>
{
    private readonly Dictionary<string, INbtTag> children = new();

    public int Count => this.children.Count;

    public NbtTagType Type => NbtTagType.Compound;

    public string Name { get; set; }

    public INbtTag Parent { get; set; }

    public INbtTag this[string name] { get => this.children[name]; set => this.Add(name, value); }

    public NbtCompound(string name = "")
    {
        if (this.Parent?.Type == NbtTagType.Compound && string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags within a compound must be named.");

        this.Name = name;
    }

    public NbtCompound(List<INbtTag> children) : this()
    {
        foreach (var child in children)
            this.Add(child.Name, child);
    }

    public NbtCompound(string name, List<INbtTag> children) : this(name)
    {
        foreach (var child in children)
            this.Add(child.Name, child);
    }

    public bool Remove(string name) => this.children.Remove(name);

    public bool HasTag(string name) => this.children.ContainsKey(name);

    public bool TryGetTag(string name, out INbtTag tag) => this.children.TryGetValue(name, out tag);

    private T GetTagValue<T>(string name)
    {
        if (this.TryGetTag(name, out var tag))
        {
            var actualTag = (NbtTag<T>)tag;

            return actualTag.Value;
        }

        return default;
    }

    public byte GetByte(string name) => this.GetTagValue<byte>(name);

    public short GetShort(string name) => this.GetTagValue<short>(name);

    public int GetInt(string name) => this.GetTagValue<int>(name);

    public long GetLong(string name) => this.GetTagValue<long>(name);

    public float GetFloat(string name) => this.GetTagValue<float>(name);

    public double GetDouble(string name) => this.GetTagValue<double>(name);

    public string GetString(string name) => this.GetTagValue<string>(name);

    public bool GetBool(string name)
    {
        if (!this.TryGetTag(name, out var tag))
            return false;

        var actualTag = (NbtTag<byte>)tag;

        return actualTag.Value == 1;
    }

    public void Clear() => this.children.Clear();

    public override string ToString()
    {
        var sb = new StringBuilder();
        var count = this.Count;

        sb.AppendLine($"TAG_Compound('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}").AppendLine("{");

        foreach (var (_, tag) in this)
            sb.AppendLine($"{tag.PrettyString()}");

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string PrettyString(int depth = 4)
    {
        var sb = new StringBuilder();
        var count = this.Count;

        var t = $"TAG_Compound('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}";

        sb.AppendLine(t.PadLeft(depth + t.Length)).AppendLine("{".PadLeft(depth * 2 + 1));

        foreach (var (_, tag) in this)
        {
            var pretty = tag.PrettyString(depth);
            sb.AppendLine($"{pretty}".PadLeft(pretty.Length + depth * 2));
        }

        sb.AppendLine("}".PadLeft(depth * 2 + 1));

        return sb.ToString();
    }

    public void Add(string name, INbtTag tag)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Tags inside a compound must be named.");

        tag.Parent = this;

        this.children.Add(name, tag);
    }

    public void Add(INbtTag tag)
    {
        if (string.IsNullOrEmpty(tag.Name))
            throw new InvalidOperationException("Tags inside a compound must be named.");

        tag.Parent = this;

        this.children.Add(tag.Name, tag);
    }

    public IEnumerator<KeyValuePair<string, INbtTag>> GetEnumerator() => this.children.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
