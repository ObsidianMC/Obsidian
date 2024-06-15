namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatTypeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required ChatElement Element { get; init; }
}
