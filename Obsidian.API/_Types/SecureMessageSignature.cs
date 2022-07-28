namespace Obsidian.API;
public struct SecureMessageSignature
{
    public DateTimeOffset Timestamp { get; init; }

    public long Salt { get; init; }

    public byte[] Value { get; init; }
}
