namespace Obsidian.API.Inventory.DataComponents;

public record class SimpleDataComponent<TValue>(DataComponentType Type, string Identifier,
    Action<INetStreamWriter, TValue> writer,
    Func<INetStreamReader, TValue> reader)
{
    private readonly Action<INetStreamWriter, TValue> writer = writer;
    private readonly Func<INetStreamReader, TValue> reader = reader;

    public TValue? Value { get; set; } = default!;

    public virtual void Read(INetStreamReader reader) => this.Value = this.reader.Invoke(reader);
    public virtual void Write(INetStreamWriter writer) => this.writer(writer, this.Value);
}

public record class TooltipSimpleDataComponent<TValue>(DataComponentType Type, string Identifier,
    Action<INetStreamWriter, TValue> writer,
    Func<INetStreamReader, TValue> reader) : SimpleDataComponent<TValue>(Type, Identifier, writer, reader)
{
    public bool ShowInTooltip { get; set; }

    public override void Read(INetStreamReader reader)
    {
        this.Value = this.reader.Invoke(reader);
        this.ShowInTooltip = reader.ReadBoolean();
    }
    public override void Write(INetStreamWriter writer)
    {
        this.writer(writer, this.Value);
        writer.WriteBoolean(this.ShowInTooltip);
    }
}

public record class SimpleDataComponent(DataComponentType Type, string Identifier) : IDataComponent
{
    public void Read(INetStreamReader reader) { }
    public void Write(INetStreamWriter writer) { }
}
