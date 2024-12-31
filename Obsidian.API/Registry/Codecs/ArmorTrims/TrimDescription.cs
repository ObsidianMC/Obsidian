namespace Obsidian.API.Registry.Codecs.ArmorTrims;
public sealed class TrimDescription : INetworkSerializable<TrimDescription>
{
    public required string Translate { get; init; }

    public string? Color { get; init; }

    public static TrimDescription Read(INetStreamReader reader)
    {
        var chat = reader.ReadChat();

        return new() { Translate = chat.Translate, Color = chat.Color?.ToString() };
    }
    public static void Write(TrimDescription value, INetStreamWriter writer) => writer.WriteChat(ChatMessage.Empty with
    {
        Translate = value.Translate,
        Color = new HexColor(value.Color)
    });
}
