namespace Obsidian.API;
public readonly record struct SecureMessageSignature
{
    public string Username { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public long Salt { get; init; }

    public ReadOnlyMemory<byte> Value { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    public bool Equals(SecureMessageSignature other) =>
        this.Username == other.Username && this.Timestamp == other.Timestamp && this.Salt == other.Salt
        && this.Value.Span.SequenceEqual(other.Value.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.Username);
        hash.Add(this.Timestamp);
        hash.Add(this.Salt);
        hash.AddBytes(this.Value.Span);
        return hash.ToHashCode();
    }
}
