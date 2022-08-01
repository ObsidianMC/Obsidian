namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatType
{
    public ChatDecoration? Decoration { get; set; }

    public string? Priority { get; set; }
}
