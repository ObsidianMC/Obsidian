namespace Obsidian.Net.ChatMessageTypes;
public sealed class PlayerChatMessageHeader
{
    public bool MessageSignaturePresent => this.MessageSignature != null;

    public byte[]? MessageSignature { get; init; }

    public required Guid Sender { get; init; }

    public required byte[] HeaderSignature { get; init; }

    public required string PlainMessage { get; init; }

    public bool FormattedMessagePresent => this.FormattedMessage != null;

    public ChatMessage? FormattedMessage { get; init; }

    public required long Timestamp { get; init; }

    public required long Salt { get; init; }
}
