using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ZombieNautilusVariant;

public sealed class ZombieNautilusVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id {  get; init; }

    public required ZombieNautilusVariantElement Element { get; init; }


    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}
