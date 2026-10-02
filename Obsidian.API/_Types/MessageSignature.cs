namespace Obsidian.API;
public readonly record struct MessageSignature
{
    public required long Salt { get; init; }

    public required ReadOnlyMemory<byte> Value { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    public bool Equals(MessageSignature other) =>
        this.Salt == other.Salt && this.Timestamp == other.Timestamp && this.Value.Span.SequenceEqual(other.Value.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.Salt);
        hash.Add(this.Timestamp);
        hash.AddBytes(this.Value.Span);
        return hash.ToHashCode();
    }
}
