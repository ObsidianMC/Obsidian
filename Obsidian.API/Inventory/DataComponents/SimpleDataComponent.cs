namespace Obsidian.API.Inventory.DataComponents;

public record class SimpleDataComponent<TValue> : DataComponent
{
    private readonly Action<INetStreamWriter, TValue> writer;
    private readonly Func<INetStreamReader, TValue> reader;

    public TValue? Value { get; set; } = default!;

    public override string Identifier { get; }
    public override DataComponentType Type { get; }

    public SimpleDataComponent(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader)
    {
        this.Type = type;
        this.Identifier = identifier;

        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public override void Read(INetStreamReader reader) => this.Value = this.reader.Invoke(reader);
    public override void Write(INetStreamWriter writer) => this.writer(writer, this.Value);
}


public record class SimpleDataComponent : DataComponent
{
    public override string Identifier { get; }
    public override DataComponentType Type { get; }

    public SimpleDataComponent(DataComponentType type, string identifier)
    {
        this.Type = type;
        this.Identifier = identifier;
    }
}
