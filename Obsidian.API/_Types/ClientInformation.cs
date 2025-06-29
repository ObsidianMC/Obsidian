namespace Obsidian.API;
public readonly record struct ClientInformation
{
    public required string Locale { get; init; }

    public required sbyte ViewDistance { get; init; }

    public required ChatVisibility ChatMode { get; init; }

    public required bool ChatColors { get; init; }

    public required PlayerBitMask DisplayedSkinParts { get; init; }

    public required MainHand MainHand { get; init; }

    public required bool EnableTextFiltering { get; init; }

    public required bool AllowServerListings { get; init; }

    public required ParticleStatus ParticleStatus { get; init; }
}
