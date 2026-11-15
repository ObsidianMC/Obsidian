namespace Obsidian.API.Registry.Codecs.Chat;
/// <summary>
/// Contains the color and italic flag applied by a chat type's formatting.
/// </summary>
public sealed record class ChatStyle
{
    /// <summary>
    /// Gets the chat text color serialized in the <c>style</c> compound.
    /// </summary>
    public required string Color { get; init; }

    /// <summary>
    /// Gets whether the formatted chat text is italic.
    /// </summary>
    public bool Italic { get; init; }
}
