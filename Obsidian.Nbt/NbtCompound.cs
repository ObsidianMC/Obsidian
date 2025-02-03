using Obsidian.Nbt.Exceptions;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Obsidian.Nbt;

public sealed class NbtCompound : INbtTag, IEnumerable<KeyValuePair<string, INbtTag>>
{
    private readonly Dictionary<string, INbtTag> children = [];

    public int Count => this.children.Count;

    public NbtTagType Type => NbtTagType.Compound;

    public string? Name { get; set; }

    public INbtTag? Parent { get; set; }

    public INbtTag this[string name] { get => this.children[name]; set => this.Add(name, value); }

    public NbtCompound(string name = "")
    {
        if (this.Parent?.Type == NbtTagType.Compound && string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name), "Tags within a compound must be named.");

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

    public bool TryGetTag(string name, [MaybeNullWhen(false)] out INbtTag tag) => this.children.TryGetValue(name, out tag);
    public bool TryGetTag<T>(string name, [MaybeNullWhen(false)] out T tag) where T : INbtTag
    {
        if (this.TryGetTag(name, out var childTag) && childTag is T matchedTag)
        {
            tag = matchedTag;
            return true;
        }

        tag = default;
        return false;
    }

    public bool TryGetTagValue<TValue>(string name, [MaybeNullWhen(false)] out TValue value)
    {
        if (this.TryGetTag<NbtTag<TValue>>(name, out var tag))
        {
            value = tag.Value;
            return true;
        }

        value = default;
        return false;
    }

    public byte GetByte(string name) => this.GetTagValue<byte>(name);

    public short GetShort(string name) => this.GetTagValue<short>(name);

    public int GetInt(string name) => this.GetTagValue<int>(name);

    public long GetLong(string name) => this.GetTagValue<long>(name);

    public float GetFloat(string name) => this.GetTagValue<float>(name);

    public double GetDouble(string name) => this.GetTagValue<double>(name);

    public string? GetString(string name) => this.GetTagValue<string>(name);

    public bool GetBool(string name) =>
        this.TryGetTag<NbtTag<byte>>(name, out var tag) && tag.Value == 1;

    public void Clear() => this.children.Clear();

    public override string ToString()
    {
        var sb = new StringBuilder();
        var count = this.Count;

        sb.AppendLine($"TAG_Compound('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}")
            .AppendLine("{");

        foreach (var (_, tag) in this)
            sb.AppendLine(tag.PrettyString());

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        var sb = new StringBuilder();
        var count = this.Count;

        var name = $"TAG_Compound('{this.Name}'): {count} {(count > 1 ? "entries" : "entry")}";

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
            throw new ArgumentNullException(nameof(name), "Tags inside a compound must be named.");

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

    public IEnumerator<KeyValuePair<string, INbtTag>> GetEnumerator() =>
        this.children.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private T? GetTagValue<T>(string name) => this.TryGetTag(name, out var tag) && tag is NbtTag<T> actualTag ? actualTag.Value : throw new TagNotFoundException(name);
}
