using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatTypeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required ChatElement Element { get; init; }

    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}
