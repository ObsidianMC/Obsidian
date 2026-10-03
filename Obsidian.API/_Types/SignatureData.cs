namespace Obsidian.API;
public readonly record struct SignatureData
{
    public required ReadOnlyMemory<byte> PublicKey { get; init; }

    public required ReadOnlyMemory<byte> Signature { get; init; }

    public required DateTimeOffset ExpirationTime { get; init; }

    // Keys and signatures compare by content, not by the memory they wrap.
    public bool Equals(SignatureData other) =>
        this.ExpirationTime == other.ExpirationTime && this.PublicKey.Span.SequenceEqual(other.PublicKey.Span)
        && this.Signature.Span.SequenceEqual(other.Signature.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(this.PublicKey.Span);
        hash.AddBytes(this.Signature.Span);
        hash.Add(this.ExpirationTime);
        return hash.ToHashCode();
    }
}
