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

    private ComponentValueKey? comparison;

    // Derived decoders use this property so a record copy never retains its previous comparison key.
    protected TValue? value
    {
        get;
        set
        {
            field = value;
            this.comparison = null;
        }
    } = default!;

    private ComponentValueKey Comparison => this.comparison ??= new(
        EqualityComparer<TValue>.Default.Equals(this.value, default) ? null : this.value);

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

    public virtual bool Equals(SimpleDataComponent<TValue>? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Type == other.Type
            && this.Identifier == other.Identifier && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Type, this.Identifier, this.Comparison);

    protected override void ReadCore(INetStreamReader reader)
    {
        var decoded = this.reader(reader);
        this.value = this.copy is null ? decoded : this.copy(decoded);
    }

    /// <summary>Writes the stored value without allocating another defensive copy.</summary>
    /// <remarks>
    /// The writer delegate and stream writer must neither mutate nor retain mutable payload arguments.
    /// </remarks>
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
