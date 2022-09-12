namespace Obsidian.API;
public readonly struct SignatureData
{
    public required byte[] PublicKey { get; init; }

    public required byte[] Signature { get; init; }

    public required DateTimeOffset ExpirationTime { get; init; }
}
