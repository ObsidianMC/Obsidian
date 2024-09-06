namespace Obsidian.Nbt;
public interface INbtTag
{
    public NbtTagType Type { get; }

    public string? Name { get; set; }

    public INbtTag? Parent { get; set; }

    public string PrettyString(int depth = 2, int addBraceDepth = 1);
}

