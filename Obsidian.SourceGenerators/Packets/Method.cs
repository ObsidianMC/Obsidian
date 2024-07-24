using Obsidian.SourceGenerators.Packets.Attributes;

namespace Obsidian.SourceGenerators.Packets;

internal sealed class Method(string name, string type, AttributeBehaviorBase[] attributes)
    : AttributeOwner(AggregateFlags(attributes) | AttributeFlags.Field, attributes)
{
    public string Name { get; } = name;
    public string Type { get; } = GetRelativeTypeName(type);

    public override string ToString()
    {
        return Name;
    }
}
