namespace Obsidian.API.Registry.Codecs.DamageTypes;
public sealed class DamageTypeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public DamageTypeElement Element { get; internal set; } = new();

    internal DamageTypeCodec() { }
}
