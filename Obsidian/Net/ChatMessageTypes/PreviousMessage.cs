namespace Obsidian.Net.ChatMessageTypes;
public readonly struct PreviousMessage
{
    public required Guid Sender { get; init; }

    public required byte[] MessageSignature { get; init; }
}
