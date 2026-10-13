namespace Obsidian.API.Inventory.DataComponents;

/// <summary>A component whose payload is immutable, or isolated with a defensive copy function.</summary>
/// <remarks>
/// Mutable payload types require a copy function that isolates every mutable child on assignment and retrieval.
/// </remarks>
public record class SimpleDataComponent<TValue> : DataComponent
{
    private readonly Action<INetStreamWriter, TValue> writer;
    private readonly Func<INetStreamReader, TValue> reader;

    private readonly Func<TValue, TValue>? copy;

    protected TValue? value = default!;

    public TValue? Value
    {
        get => this.copy is null ? this.value : this.copy(this.value);
        init => this.value = this.copy is null ? value : this.copy(value);
    }

    public override string Identifier { get; }
    public override DataComponentType Type { get; }

    public SimpleDataComponent(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader,
        Func<TValue, TValue>? copy = null)
    {
        this.Type = type;
        this.copy = copy;
        this.Identifier = identifier;

        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    protected override void ReadCore(INetStreamReader reader) => this.value = this.reader.Invoke(reader);
    public override void Write(INetStreamWriter writer) => this.writer(writer, this.value);
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
