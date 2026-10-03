namespace Obsidian.API;
public readonly record struct SignedMessage
{
    public required Guid UserId { get; init; }

    public required ReadOnlyMemory<byte> Signature { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    public bool Equals(SignedMessage other) =>
        this.UserId == other.UserId && this.Signature.Span.SequenceEqual(other.Signature.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.UserId);
        hash.AddBytes(this.Signature.Span);
        return hash.ToHashCode();
    }
}
