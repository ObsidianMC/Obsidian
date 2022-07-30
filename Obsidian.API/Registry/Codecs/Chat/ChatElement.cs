namespace Obsidian.API.Registry.Codecs.Chat;

public sealed class ChatElement
{
    public ChatType? Chat { get; set; }

    public ChatType? Narration { get; set; }

    public object? Overlay { get; set; }
}
