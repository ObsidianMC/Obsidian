namespace Obsidian.API.Inventory;
/// <summary>An immutable item component. Replace it on the stack to change its value.</summary>
public abstract record DataComponent
{
    /// <summary>
    /// Gets the component's type, which a stack holds at most one component of.
    /// </summary>
    public abstract DataComponentType Type { get; }

    /// <summary>
    /// Gets the component's identifier, like <c>minecraft:max_stack_size</c>, which it's saved under.
    /// </summary>
    public abstract string Identifier { get; }

    /// <summary>
    /// Writes the component's value as clients read it, without its type id. Writes nothing by default.
    /// </summary>
    public virtual void Write(INetStreamWriter writer) { }

    /// <summary>Reads a new value without changing this component.</summary>
    public DataComponent Read(INetStreamReader reader)
    {
        ComponentExecutionGuard.CheckOverride(this);
        var component = this with { };
        component.ReadCore(reader);
        return component;
    }

    /// <summary>Decodes into the new, unpublished copy created by <see cref="Read"/>.</summary>
    /// <remarks>Replace fields rather than mutating references shared with the original value.</remarks>
    protected virtual void ReadCore(INetStreamReader reader) { }
}
