namespace Obsidian.API;
public readonly struct MessageSignature
{
    public required long Salt { get; init; }

    public required byte[] Value { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}
