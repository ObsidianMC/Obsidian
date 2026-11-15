namespace Obsidian.API;
/// <summary>A signed chat message the client acknowledges having seen, given by its sender and signature.</summary>
public readonly record struct SignedMessage
{
    /// <summary>Gets the sender's UUID.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the message's signature bytes.</summary>
    public required ReadOnlyMemory<byte> Signature { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    /// <summary>Indicates whether both have the same sender and signature bytes.</summary>
    public bool Equals(SignedMessage other) =>
        this.UserId == other.UserId && this.Signature.Span.SequenceEqual(other.Signature.Span);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.UserId);
        hash.AddBytes(this.Signature.Span);
        return hash.ToHashCode();
    }
}
