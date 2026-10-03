namespace Obsidian.API;
public readonly record struct ArgumentSignature
{
    public required string ArgumentName { get; init; }

    public int SignatureLength => this.Signature.Length;

    public required ReadOnlyMemory<byte> Signature { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    public bool Equals(ArgumentSignature other) =>
        this.ArgumentName == other.ArgumentName && this.Signature.Span.SequenceEqual(other.Signature.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.ArgumentName);
        hash.AddBytes(this.Signature.Span);
        return hash.ToHashCode();
    }
}
