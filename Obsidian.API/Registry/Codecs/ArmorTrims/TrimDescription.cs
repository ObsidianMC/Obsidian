namespace Obsidian.API.Registry.Codecs.ArmorTrims;
/// <summary>
/// Represents the translated display text and optional color of an armor trim.
/// </summary>
public sealed record class TrimDescription : INetworkSerializable<TrimDescription>
{
    /// <summary>
    /// Gets the translation key used for the trim description.
    /// </summary>
    public required string Translate { get; init; }

    /// <summary>
    /// Gets the optional text color, or <c>null</c> when no color is specified.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Reads a chat component and retains its translation key and color as a trim description.
    /// </summary>
    public static TrimDescription Read(INetStreamReader reader)
    {
        var chat = reader.ReadChat();

        return new() { Translate = chat.Translate, Color = chat.Color?.ToString() };
    }
    /// <summary>
    /// Writes the trim description as a translated chat component with its optional color.
    /// </summary>
    public static void Write(TrimDescription value, INetStreamWriter writer) => writer.WriteChat(ChatMessage.Empty with
    {
        Translate = value.Translate,
        Color = new HexColor(value.Color)
    });
}
