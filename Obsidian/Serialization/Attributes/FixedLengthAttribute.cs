namespace Obsidian.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class FixedLengthAttribute : Attribute
{
    public FixedLengthAttribute(int length)
    {
    }
}
