namespace Obsidian.API.Inventory;
/// <summary>An immutable item component. Replace it on the stack to change its value.</summary>
public abstract record DataComponent
{
    public abstract DataComponentType Type { get; }

    public abstract string Identifier { get; }

    public virtual void Write(INetStreamWriter writer) { }

    /// <summary>Reads a new value without changing this component.</summary>
    public DataComponent Read(INetStreamReader reader)
    {
        var component = this with { };
        component.ReadCore(reader);
        return component;
    }

    /// <summary>Decodes into the new, unpublished copy created by <see cref="Read"/>.</summary>
    /// <remarks>Replace fields rather than mutating references shared with the original value.</remarks>
    protected virtual void ReadCore(INetStreamReader reader) { }
}
