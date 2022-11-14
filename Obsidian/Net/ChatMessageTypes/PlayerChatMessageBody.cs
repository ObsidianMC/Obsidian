using Obsidian.API._Types;

namespace Obsidian.Net.ChatMessageTypes;
public sealed class PlayerChatMessageBody
{
    public bool UnsignedContentPresent => this.UnsignedContent != null;

    public ChatMessage? UnsignedContent { get; init; }

    public required ChatFilterType FilterType { get; init; }

    public BitSet? FilterTypeBytes { get; init; }
}

