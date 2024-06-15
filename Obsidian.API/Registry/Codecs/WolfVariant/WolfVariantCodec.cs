﻿namespace Obsidian.API.Registry.Codecs.WolfVariant;
public sealed record class WolfVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required WolfVariantElement Element { get; init; }
}
