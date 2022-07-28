namespace Obsidian.API.Registry.Codecs.Chat;

public sealed class ChatDecoration
{
    public List<string> Parameters { get; set; } = new();

    public string TranslationKey { get; set; }

    public object? Style { get; set; }
}
