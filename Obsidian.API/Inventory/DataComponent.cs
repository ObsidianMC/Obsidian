namespace Obsidian.API.Inventory;
public abstract record DataComponent
{
    public abstract DataComponentType Type { get; }

    public abstract string Identifier { get; }

    public virtual void Write(INetStreamWriter writer) { }

    public virtual void Read(INetStreamReader reader) { }
}
