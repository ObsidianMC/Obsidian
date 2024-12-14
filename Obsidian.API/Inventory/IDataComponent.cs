namespace Obsidian.API.Inventory;
public interface IDataComponent
{
    public DataComponentType Type { get; }

    public string Identifier { get; }

    public void Write(INetStreamWriter writer);

    public void Read(INetStreamReader reader);
}
