using Obsidian.SourceGenerators.Packets.Attributes;
using System;
using System.Linq;

namespace Obsidian.SourceGenerators.Packets;

internal sealed class Method : AttributeOwner
{
    public string Name { get; }
    public string Type { get; }

    public Method(string name, string type, AttributeBehaviorBase[] attributes)
    {
        Name = name;
        Type = type;
        Attributes = attributes;

        if (Type.Contains('.'))
        {
            Type = Type.Substring(Type.LastIndexOf('.') + 1);
        }

        Flags = AttributeFlags.Field;
        for (int i = 0; i < attributes.Length; i++)
        {
            Flags |= attributes[i].Flag;
        }
    }

    public override string ToString()
    {
        return Name;
    }
}
