namespace Obsidian.API;
/// <summary>A player's signature over one argument of a signed command, such as the message of <c>/msg</c>.</summary>
public readonly record struct ArgumentSignature
{
    /// <summary>Gets the name of the signed argument.</summary>
    public required string ArgumentName { get; init; }

    /// <summary>Gets the signature's length in bytes.</summary>
    public int SignatureLength => this.Signature.Length;

    /// <summary>Gets the signature bytes.</summary>
    public required ReadOnlyMemory<byte> Signature { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    /// <summary>Indicates whether both signatures have the same argument name and signature bytes.</summary>
    public bool Equals(ArgumentSignature other) =>
        this.ArgumentName == other.ArgumentName && this.Signature.Span.SequenceEqual(other.Signature.Span);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.ArgumentName);
        hash.AddBytes(this.Signature.Span);
        return hash.ToHashCode();
    }
}
