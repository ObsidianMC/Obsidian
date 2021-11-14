using Obsidian.SourceGenerators.Packets.Attributes;

namespace Obsidian.SourceGenerators.Packets;

internal abstract class AttributeOwner
{
    public AttributeFlags Flags { get; set; }
    public AttributeBehaviorBase[] Attributes { get; set; }

    public bool TryGetAttribute<TAttribute>(out TAttribute attribute) where TAttribute : AttributeBehaviorBase
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
}
