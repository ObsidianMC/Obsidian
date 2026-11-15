namespace Obsidian.API.Registry.Codecs.Chat;

/// <summary>
/// Describes optional translation parameters and style data for a chat decoration.
/// </summary>
public sealed record class ChatDecoration
{
    /// <summary>
    /// Gets or sets the ordered names of values substituted into the translated message.
    /// </summary>
    public List<string>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the client translation key used for the decoration.
    /// </summary>
    public string? TranslationKey { get; set; }

    /// <summary>
    /// Gets or sets optional style data associated with the decoration.
    /// </summary>
    public object? Style { get; set; }
}
