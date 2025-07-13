using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;
public sealed record class DialogCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required DialogElement Element { get; init; }

    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}
