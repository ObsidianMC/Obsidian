namespace Obsidian.API.Registry.Codecs.DamageTypes;
public sealed class DamageTypeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required DamageTypeElement Element { get; init; }
}

