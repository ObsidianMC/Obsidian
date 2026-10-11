namespace Obsidian.Net.ChatMessageTypes;
public sealed class PlayerChatMessageFormatting
{
    public required int ChatType { get; init; }

    public required ChatMessage SenderName { get; init; }

    public bool TargetNamePresent => this.TargetName is not null;

    public ChatMessage? TargetName { get; init; }
}
