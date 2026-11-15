namespace Obsidian.API;
/// <summary>The signature of a signed chat message.</summary>
public readonly record struct MessageSignature
{
    /// <summary>Gets the random salt mixed into the signature.</summary>
    public required long Salt { get; init; }

    /// <summary>Gets the signature bytes.</summary>
    public required ReadOnlyMemory<byte> Value { get; init; }

    /// <summary>Gets when the message was signed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    /// <summary>Indicates whether both signatures have the same salt, timestamp and signature bytes.</summary>
    public bool Equals(MessageSignature other) =>
        this.Salt == other.Salt && this.Timestamp == other.Timestamp && this.Value.Span.SequenceEqual(other.Value.Span);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.Salt);
        hash.Add(this.Timestamp);
        hash.AddBytes(this.Value.Span);
        return hash.ToHashCode();
    }
}
