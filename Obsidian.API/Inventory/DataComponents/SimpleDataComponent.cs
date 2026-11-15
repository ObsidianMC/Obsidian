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
    /// <summary>
    /// The stored value, which derived components set as they read one.
    /// </summary>
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

    /// <summary>
    /// Gets the component's value, copied when the component has a copy function.
    /// </summary>
    public TValue? Value
    {
        get => this.Copy(this.value);
        init => this.value = this.Copy(value);
    }

    /// <summary>
    /// The stored value, without the defensive copy <see cref="Value"/> makes, which may run a plugin's delegate. For
    /// code holding an inventory's lock, which must not run plugin code; only for values it reads and never keeps.
    /// </summary>
    internal TValue? StoredValue => this.value;

    private TValue? Copy(TValue? value)
    {
        if (this.copy is null)
            return value;

        ComponentExecutionGuard.CheckDelegate(this.copy, "copy");
        return this.copy(value);
    }

    /// <inheritdoc/>
    public override string Identifier { get; }
    /// <inheritdoc/>
    public override DataComponentType Type { get; }

    /// <summary>
    /// Creates a component of <paramref name="type"/> that writes and reads its value with the given delegates.
    /// </summary>
    /// <param name="type">The component's type.</param>
    /// <param name="identifier">The component's identifier, like <c>minecraft:max_stack_size</c>.</param>
    /// <param name="writer">Writes a value as clients read it.</param>
    /// <param name="reader">Reads a value as clients write it.</param>
    /// <param name="copy">Copies a mutable value, so that the component never shares it; null for immutable
    /// values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="reader"/> is
    /// null.</exception>
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

    /// <inheritdoc/>
    public virtual bool Equals(SimpleDataComponent<TValue>? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Type == other.Type
            && this.Identifier == other.Identifier && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Type, this.Identifier, this.Comparison);

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        ComponentExecutionGuard.CheckDelegate(this.reader, "network reader");
        var decoded = this.reader(reader);
        this.value = this.Copy(decoded);
    }

    /// <summary>Writes the stored value without allocating another defensive copy.</summary>
    /// <remarks>
    /// The writer delegate and stream writer must neither mutate nor retain mutable payload arguments.
    /// </remarks>
    public override void Write(INetStreamWriter writer)
    {
        ComponentExecutionGuard.CheckDelegate(this.writer, "network writer");
        this.writer(writer, this.value);
    }
}


/// <summary>
/// A component without a value, whose presence is what counts, like <c>minecraft:glider</c>.
/// </summary>
public record class SimpleDataComponent : DataComponent
{
    /// <inheritdoc/>
    public override string Identifier { get; }
    /// <inheritdoc/>
    public override DataComponentType Type { get; }

    /// <summary>
    /// Creates a marker component of <paramref name="type"/>.
    /// </summary>
    public SimpleDataComponent(DataComponentType type, string identifier)
    {
        this.Type = type;
        this.Identifier = identifier;
    }
}
