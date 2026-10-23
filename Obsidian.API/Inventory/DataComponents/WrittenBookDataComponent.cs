namespace Obsidian.API.Inventory.DataComponents;

public sealed record WrittenBookDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.WrittenBookContent;
    public override string Identifier => "minecraft:written_book_content";
    private string title = "";

    public string Title
    {
        get => this.title;
        init => this.title = value;
    }

    private string? filteredTitle;

    public string? FilteredTitle
    {
        get => this.filteredTitle;
        init => this.filteredTitle = value;
    }

    private string author = "";

    public string Author
    {
        get => this.author;
        init => this.author = value;
    }

    private int generation;

    public int Generation
    {
        get => this.generation;
        init => this.generation = value;
    }

    private ImmutableArray<(ChatMessage Raw, ChatMessage? Filtered)> pages = [];

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

    public bool Resolved
    {
        get => this.resolved;
        init => this.resolved = value;
    }

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
