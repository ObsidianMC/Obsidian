namespace Obsidian.API.Inventory.DataComponents;

public sealed record WrittenBookDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.WrittenBookContent;
    public override string Identifier => "minecraft:written_book_content";
    public string Title { get; set; } = "";
    public string? FilteredTitle { get; set; }
    public string Author { get; set; } = "";
    public int Generation { get; set; }
    public (ChatMessage Raw, ChatMessage? Filtered)[] Pages { get; set; } = [];
    public bool Resolved { get; set; }

    public override void Read(INetStreamReader reader)
    {
        this.Title = reader.ReadString();
        this.FilteredTitle = reader.ReadOptionalString();
        this.Author = reader.ReadString();
        this.Generation = reader.ReadVarInt();
        this.Pages = reader.ReadLengthPrefixedArray(() => (reader.ReadChat(), reader.ReadBoolean() ? reader.ReadChat() : null));
        this.Resolved = reader.ReadBoolean();
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
        }, this.Pages);
        writer.WriteBoolean(this.Resolved);
    }
}
