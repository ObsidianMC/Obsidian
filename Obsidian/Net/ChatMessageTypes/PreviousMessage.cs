namespace Obsidian.Net.ChatMessageTypes;
public readonly record struct PreviousMessage
{
    public required Guid Sender { get; init; }

    public required ReadOnlyMemory<byte> MessageSignature { get; init; }

    // Signatures compare by content, not by the memory they wrap.
    public bool Equals(PreviousMessage other) =>
        this.Sender == other.Sender && this.MessageSignature.Span.SequenceEqual(other.MessageSignature.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(this.Sender);
        hash.AddBytes(this.MessageSignature.Span);
        return hash.ToHashCode();
    }
}
