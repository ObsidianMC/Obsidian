namespace Obsidian.API;
public readonly struct MessageSigningData
{
    public required long Salt { get; init; }

    public required byte[] MessageSignature { get; init; }
}
