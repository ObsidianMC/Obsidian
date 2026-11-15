namespace Obsidian.API;
/// <summary>A player's chat session key, which the client sends so the server can check its signed messages.</summary>
public readonly record struct SignatureData
{
    /// <summary>Gets the session's public key.</summary>
    public required ReadOnlyMemory<byte> PublicKey { get; init; }

    /// <summary>Gets Mojang's signature of the public key.</summary>
    public required ReadOnlyMemory<byte> Signature { get; init; }

    /// <summary>Gets when the key expires.</summary>
    public required DateTimeOffset ExpirationTime { get; init; }

    // Keys and signatures compare by content, not by the memory they wrap.
    /// <summary>Indicates whether both keys have the same public key, signature and expiry.</summary>
    public bool Equals(SignatureData other) =>
        this.ExpirationTime == other.ExpirationTime && this.PublicKey.Span.SequenceEqual(other.PublicKey.Span)
        && this.Signature.Span.SequenceEqual(other.Signature.Span);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(this.PublicKey.Span);
        hash.AddBytes(this.Signature.Span);
        hash.Add(this.ExpirationTime);
        return hash.ToHashCode();
    }
}
