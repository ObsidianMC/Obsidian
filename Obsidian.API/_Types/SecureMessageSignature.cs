namespace Obsidian.API;
/// <summary>
/// The signature of a player's chat message, with its sender's name, when it was signed and its salt.
/// </summary>
public readonly record struct SecureMessageSignature
{
    /// <summary>Gets the sender's username.</summary>
    public string Username { get; init; }

    /// <summary>Gets when the message was signed.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the random salt mixed into the signature.</summary>
    public long Salt { get; init; }

    /// <summary>Gets the signature bytes.</summary>
    public ReadOnlyMemory<byte> Value { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    /// <summary>
    /// Indicates whether both signatures have the same username, timestamp, salt and signature bytes.
    /// </summary>
    public bool Equals(SecureMessageSignature other) =>
        this.Username == other.Username && this.Timestamp == other.Timestamp && this.Salt == other.Salt
        && this.Value.Span.SequenceEqual(other.Value.Span);

    /// <inheritdoc/>
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
