using Obsidian.SourceGenerators.Packets.Attributes;

namespace Obsidian.SourceGenerators.Packets;

internal abstract class AttributeOwner(AttributeFlags flags, AttributeBehaviorBase[] attributes)
{
    public AttributeFlags Flags { get; } = flags;
    public AttributeBehaviorBase[] Attributes { get; } = attributes;

    public bool TryGetAttribute<TAttribute>(out TAttribute? attribute) where TAttribute : AttributeBehaviorBase
    {
        for (int i = 0; i < Attributes.Length; i++)
        {
            if (Attributes[i] is TAttribute tAttribute)
            {
                attribute = tAttribute;
                return true;
            }
        }

        attribute = default;
        return false;
    }

    protected static AttributeFlags AggregateFlags(AttributeBehaviorBase[] attributes)
    {
        var flags = AttributeFlags.None;
        foreach (AttributeBehaviorBase attribute in attributes)
        {
            flags |= attribute.Flag;
        }
        return flags;
    }

    protected static string GetRelativeTypeName(string typeName)
    {
        int dotIndex = typeName.LastIndexOf('.');
        return dotIndex >= 0 ? typeName.Substring(dotIndex + 1) : typeName;
    }
}
