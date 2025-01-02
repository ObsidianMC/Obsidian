namespace Obsidian.API.Inventory.DataComponents;

public record class SimpleDataComponent<TValue>(DataComponentType Type, string Identifier,
    Action<INetStreamWriter, TValue> writer,
    Func<INetStreamReader, TValue> reader) : SimpleDataComponent(Type, Identifier)
{
    private readonly Action<INetStreamWriter, TValue> writer = writer;
    private readonly Func<INetStreamReader, TValue> reader = reader;

    public TValue? Value { get; set; } = default!;

    public override void Read(INetStreamReader reader) => this.Value = this.reader.Invoke(reader);
    public override void Write(INetStreamWriter writer) => this.writer(writer, this.Value);
}

public record class TooltipSimpleDataComponent<TValue> : SimpleDataComponent<TValue>
{
    public bool ShowInTooltip { get; set; }

    public TooltipSimpleDataComponent(DataComponentType Type, string Identifier,
        Action<INetStreamWriter, TValue> writer, Func<INetStreamReader, TValue> reader,
        bool showInTooltip = false) : base(Type, Identifier, writer, reader)
    {
        this.ShowInTooltip = showInTooltip;
    }

    public override void Read(INetStreamReader reader)
    {
        base.Read(reader);
        this.ShowInTooltip = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteBoolean(this.ShowInTooltip);
    }
}

public record class SimpleDataComponent(DataComponentType Type, string Identifier) : IDataComponent
{
    public virtual void Read(INetStreamReader reader) { }
    public virtual void Write(INetStreamWriter writer) { }
}
