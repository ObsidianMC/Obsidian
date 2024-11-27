namespace Obsidian.Net.ChatMessageTypes;
public sealed class PlayerChatMessageBody
{
    public required string Content { get; init; }

    public required MessageSignature Signature { get; init; }

    public List<MessageSignature> LastSeenMessages { get; init; } = [];
}

