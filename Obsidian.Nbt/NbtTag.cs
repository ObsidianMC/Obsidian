namespace Obsidian.Nbt;

public class NbtTag<T> : INbtTag
{
    public NbtTagType Type { get; }

    public string Name { get; set; }

    /// <summary>
    /// This is either null, a compound or list
    /// </summary>
    public INbtTag Parent { get; set; }

    public T Value { get; }

    public NbtTag(string name, T value, INbtTag parent = null)
    {
        Name = name;
        Parent = parent;
        Value = value;
        Type = value switch
        {
            bool => NbtTagType.Byte,
            byte => NbtTagType.Byte,
            short => NbtTagType.Short,
            int => NbtTagType.Int,
            long => NbtTagType.Long,
            float => NbtTagType.Float,
            double => NbtTagType.Double,
            string => NbtTagType.String,
            _ => throw new InvalidOperationException()
        };
    }

    public override string ToString()
    {
        switch (Type)
        {
            case NbtTagType.Byte:
            case NbtTagType.Short:
            case NbtTagType.Int:
            case NbtTagType.Long:
            case NbtTagType.Float:
            case NbtTagType.Double:
            case NbtTagType.String:
                return $"TAG_{Type}('{Name}'): {Value}";
            default:
                throw new NotSupportedException("Only generic types are supported.");
        }
    }

    public string PrettyString(int depth = 2, int addBraceDepth = 1)
    {
        switch (Type)
        {
            case NbtTagType.Byte:
            case NbtTagType.Short:
            case NbtTagType.Int:
            case NbtTagType.Long:
            case NbtTagType.Float:
            case NbtTagType.Double:
            case NbtTagType.String:
                {
                    var name = $"TAG_{Type}('{Name}'): {Value}";
                    return name.PadLeft(name.Length + depth);
                }
            default:
                throw new NotSupportedException("Only generic types are supported.");
        }
    }
}

public interface INbtTag
{
    public NbtTagType Type { get; }

    public string Name { get; set; }

    public INbtTag Parent { get; set; }

    public string PrettyString(int depth = 2, int addBraceDepth = 1);
}
