namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatType
{
    public required List<string> Parameters { get; init; }

    public ChatStyle? Style { get; init; }

    public required string TranslationKey { get; init; }
}
