namespace Obsidian.API.Inventory.DataComponents;
public class SimpleDataComponent<TValue>(DataComponentType type, string identifier, Action<INetStreamWriter, TValue> writer,
    Func<INetStreamReader, TValue> reader) : IDataComponent
{
    private readonly Action<INetStreamWriter, TValue> writer = writer;
    private readonly Func<INetStreamReader, TValue> reader = reader;

    public TValue Value { get; set; } = default!;

    public DataComponentType Type { get; } = type;

    public string Identifier { get; } = identifier;

    public void Read(INetStreamReader reader) => this.Value = this.reader.Invoke(reader);
    public void Write(INetStreamWriter writer) => this.writer(writer, this.Value);
}
