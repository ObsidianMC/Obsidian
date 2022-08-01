namespace Obsidian.API;
public readonly struct SecureMessageSignature
{
    public string Username { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public long Salt { get; init; }

    public byte[] Value { get; init; }
}
