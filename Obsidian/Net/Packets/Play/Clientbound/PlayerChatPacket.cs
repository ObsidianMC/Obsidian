using Obsidian.Net.ChatMessageTypes;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO this changed implement later. 
public partial class PlayerChatPacket
{
    [Field(0)]
    public required Guid Sender { get; init; }

    [Field(1), VarLength]
    public required int Index { get; init; }

    [Field(2)]
    //This needs to be read up on more
    public required byte[] MessageSignature { get; init; }

    [Field(3)]
    public required ChatMessage UnsignedContent { get; init; } 

    [Field(4)]
    public required PlayerChatMessageBody Body { get; init; }

    [Field(5)]
    public required PlayerChatMessageFormatting ChatFormatting { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteUuid(this.Sender);
        writer.WriteVarInt(this.Index);

        writer.WriteByteArray(this.MessageSignature);
    }
}
