using Obsidian.Net.ChatMessageTypes;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO this changed implement later
public partial class PlayerChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public required PlayerChatMessageHeader Header { get; init; }

    [Field(1)]
    public required PlayerChatMessageBody Body { get; init; }

    [Field(2), VarLength]
    public required List<PreviousMessage> PreviousMessages { get; init; }

    [Field(3)]
    public required object Other { get; init; }

    [Field(4)]
    public required PlayerChatMessageFormatting ChatFormatting { get; init; }

    public int Id => 0x39;

    public void Serialize(MinecraftStream stream)
    {
        this.WriteHeader(stream);

        if (this.PreviousMessages.Count > 5)
            throw new InvalidOperationException("PreviousMessages must have a total of 5 elements.");

        stream.WriteVarInt(this.PreviousMessages.Count);
        foreach (var previousMessage in this.PreviousMessages)
        {
            stream.WriteUuid(previousMessage.Sender);
            stream.WriteVarInt(previousMessage.MessageSignature.Length);
            stream.WriteByteArray(previousMessage.MessageSignature);
        }

        this.WriteBody(stream);

        this.WriteNetworkTarget(stream);
    }

    private void WriteNetworkTarget(MinecraftStream stream)
    {
        stream.WriteVarInt(this.ChatFormatting.ChatType);

        stream.WriteChat(this.ChatFormatting.SenderName);

        stream.WriteBoolean(this.ChatFormatting.TargetNamePresent);

        if (this.ChatFormatting.TargetNamePresent)
            stream.WriteChat(this.ChatFormatting.TargetName);
    }

    private void WriteBody(MinecraftStream stream)
    {
        stream.WriteBoolean(this.Body.UnsignedContentPresent);

        if (this.Body.UnsignedContentPresent)
            stream.WriteChat(this.Body.UnsignedContent);

        stream.WriteVarInt(this.Body.FilterType);
        if (this.Body.FilterType.HasFlag(ChatFilterType.PartiallyFiltered))
        {
            stream.WriteVarInt(this.Body.FilterTypeBytes.DataStorage.Length);
            stream.WriteLongArray(this.Body.FilterTypeBytes.DataStorage.ToArray());
        }
    }

    private void WriteHeader(MinecraftStream stream)
    {
        stream.WriteBoolean(this.Header.MessageSignaturePresent);
        if (this.Header.MessageSignaturePresent)
        {
            stream.WriteVarInt(this.Header.MessageSignature.Length);
            stream.WriteByteArray(this.Header.MessageSignature);
        }

        stream.WriteUuid(this.Header.Sender);

        stream.WriteVarInt(this.Header.HeaderSignature.Length);
        stream.WriteByteArray(this.Header.HeaderSignature);

        stream.WriteString(this.Header.PlainMessage, 256);

        stream.WriteBoolean(this.Header.FormattedMessagePresent);
        if (this.Header.FormattedMessagePresent)
            stream.WriteChat(this.Header.FormattedMessage);

        stream.WriteLong(this.Header.Timestamp);
        stream.WriteLong(this.Header.Salt);
    }
}
