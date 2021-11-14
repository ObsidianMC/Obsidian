using System;

namespace Obsidian.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class DataFormatAttribute : Attribute
{
    public Type Type { get; }

    public DataFormatAttribute(Type type)
    {
        Type = type;
    }
}
