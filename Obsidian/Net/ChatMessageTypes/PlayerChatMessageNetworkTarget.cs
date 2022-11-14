namespace Obsidian.Net.ChatMessageTypes;
public sealed class PlayerChatMessageNetworkTarget
{
    public required int ChatType { get; init; }

    public required ChatMessage NetworkName { get; init; }

    public bool TargetNamePresent => this.TargetName != null;

    public ChatMessage? TargetName { get; init; }
}
