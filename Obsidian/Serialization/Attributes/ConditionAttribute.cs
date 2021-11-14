using System;

namespace Obsidian.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class ConditionAttribute : Attribute
{
    public ConditionAttribute(string condition)
    {
    }
}
