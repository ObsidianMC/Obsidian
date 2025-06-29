namespace Obsidian.Serialization.Attributes;

/// <summary>
/// Defines the attributes of a field in a packet.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FieldAttribute"/> class.
    /// </summary>
    /// <param name="order">The order of the field when serializing or deserializing the packet. Starts from 0.</param>
    public FieldAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// The order of the field when serializing or deserializing the packet. Starts from 0.
    /// </summary>
    public int Order { get; }
}
