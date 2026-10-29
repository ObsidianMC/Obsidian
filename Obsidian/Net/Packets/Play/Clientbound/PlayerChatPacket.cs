using Obsidian.Net.ChatMessageTypes;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerChatPacket
{
    public int GlobalIndex { get; init; }

    public ChatFilterType FilterType { get; init; }

    public long[] FilterMask { get; init; } = [];
    [Field(0)]
    public required Guid Sender { get; init; }

    [Field(1), VarLength]
    public required int Index { get; init; }

    [Field(2)]
    public required ReadOnlyMemory<byte> MessageSignature { get; init; }

    [Field(3)]
    public ChatMessage? UnsignedContent { get; init; }

    [Field(4)]
    public required PlayerChatMessageBody Body { get; init; }

    [Field(5)]
    public required PlayerChatMessageFormatting ChatFormatting { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.GlobalIndex);
        writer.WriteUuid(this.Sender);
        writer.WriteVarInt(this.Index);

        writer.WriteBoolean(!this.MessageSignature.IsEmpty);
        if (!this.MessageSignature.IsEmpty)
        {
            if (this.MessageSignature.Length != 256)
                throw new ArgumentException("A chat signature must contain 256 bytes.");
            writer.WriteByteArray(this.MessageSignature.Span);
        }
        writer.WriteString(this.Body.Content);
        writer.WriteLong(this.Body.Signature.Timestamp.ToUnixTimeMilliseconds());
        writer.WriteLong(this.Body.Signature.Salt);
        var lastSeen = this.Body.PackedLastSeen ?? this.Body.LastSeenMessages
            .Select(signature => new PackedChatSignature(-1, signature.Value)).ToArray();
        writer.WriteVarInt(lastSeen.Count);
        foreach (var signature in lastSeen)
            signature.Write(writer);
        writer.WriteOptional(this.UnsignedContent);
        writer.WriteVarInt((int)this.FilterType);
        if (this.FilterType == ChatFilterType.PartiallyFiltered)
        {
            // ByteBufCodecs.BIT_SET is a trimmed little-endian byte array.
            var length = this.FilterMask.Length * 8;
            while (length > 0 && MaskByte(length - 1) == 0)
                length--;
            writer.WriteVarInt(length);
            for (var i = 0; i < length; i++)
                writer.WriteByte(MaskByte(i));
        }
        writer.WriteRegistryHolder("minecraft:chat_type", this.ChatFormatting.ChatType);
        writer.WriteChat(this.ChatFormatting.SenderName);
        writer.WriteOptional(this.ChatFormatting.TargetName);

        byte MaskByte(int index) => (byte)((ulong)this.FilterMask[index / 8] >> (index % 8 * 8));
    }
}
