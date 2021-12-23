namespace Obsidian.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    public FieldAttribute(int order)
    {
    }
}
