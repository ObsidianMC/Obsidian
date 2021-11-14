using System;

namespace Obsidian.Serialization.Attributes;

/// <summary>
/// Defines the type of count prefix for serialized collections. When left out, VarInt is used by default.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CountTypeAttribute : Attribute
{
    public CountTypeAttribute(Type type)
    {
    }
}
