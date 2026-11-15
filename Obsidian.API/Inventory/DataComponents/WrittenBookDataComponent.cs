namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// A written book's title, author and pages (<c>minecraft:written_book_content</c>).
/// </summary>
public sealed record WrittenBookDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.title,
        this.filteredTitle,
        this.author,
        this.generation,
        ComponentValueKey.Sequence(this.pages),
        this.resolved
    });

    /// <inheritdoc/>
    public bool Equals(WrittenBookDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.WrittenBookContent;
    /// <inheritdoc/>
    public override string Identifier => "minecraft:written_book_content";
    private string title = "";

    /// <summary>
    /// Gets the book's title.
    /// </summary>
    public string Title
    {
        get => this.title;
        init => this.title = value;
    }

    private string? filteredTitle;

    /// <summary>
    /// Gets the title with profanity filtered, for players with filtering on, or null when it's the same.
    /// </summary>
    public string? FilteredTitle
    {
        get => this.filteredTitle;
        init => this.filteredTitle = value;
    }

    private string author = "";

    /// <summary>
    /// Gets the name of the book's author.
    /// </summary>
    public string Author
    {
        get => this.author;
        init => this.author = value;
    }

    private int generation;

    /// <summary>
    /// Gets how many times the book was copied: 0 for the original, up to 3.
    /// </summary>
    public int Generation
    {
        get => this.generation;
        init => this.generation = value;
    }

    private ImmutableArray<(ChatMessage Raw, ChatMessage? Filtered)> pages = [];

    /// <summary>
    /// Gets the pages, each with its filtered text or null when it's the same.
    /// </summary>
    public ImmutableArray<(ChatMessage Raw, ChatMessage? Filtered)> Pages
    {
        get => this.pages
            .Select(page => (ComponentCopies.Chat(page.Raw)!, ComponentCopies.Chat(page.Filtered)))
            .ToImmutableArray();
        init => this.pages = value
            .Select(page => (ComponentCopies.Chat(page.Raw)!, ComponentCopies.Chat(page.Filtered)))
            .ToImmutableArray();
    }

    private bool resolved;

    /// <summary>
    /// Gets whether the pages' text components, like selectors, were already resolved.
    /// </summary>
    public bool Resolved
    {
        get => this.resolved;
        init => this.resolved = value;
    }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.title = reader.ReadString();
        this.filteredTitle = reader.ReadOptionalString();
        this.author = reader.ReadString();
        this.generation = reader.ReadVarInt();
        this.pages = reader.ReadLengthPrefixedArray(() =>
            (reader.ReadChat(), reader.ReadBoolean() ? reader.ReadChat() : null)).ToImmutableArray();
        this.resolved = reader.ReadBoolean();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteString(this.Title);
        writer.WriteOptional(this.FilteredTitle);
        writer.WriteString(this.Author);
        writer.WriteVarInt(this.Generation);
        writer.WriteLengthPrefixedArray(page =>
        {
            writer.WriteChat(page.Raw);
            writer.WriteBoolean(page.Filtered is not null);
            if (page.Filtered is not null)
                writer.WriteChat(page.Filtered);
        }, this.pages.AsSpan());
        writer.WriteBoolean(this.Resolved);
    }
}
