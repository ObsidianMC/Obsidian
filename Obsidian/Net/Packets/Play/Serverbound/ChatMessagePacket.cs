using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatMessagePacket : IServerboundPacket
{
    [Field(0)]
    public string Message { get; private set; }

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(4)]
    public byte[] Signature { get; private set; }

    [Field(5)]
    public bool SignedPreview { get; set; }

    [Field(6)]
    public List<SignedMessage> PreviouslySeenMessages { get; private set; }

    [Field(7)]
    public bool HasLastMessage { get; private set; }

    [Field(8), Condition("HasLastMessage")]
    public SignedMessage LastMessage { get; private set; }


    public int Id => 0x06;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await server.HandleIncomingMessageAsync(this, player.client);
    }

    public void Populate(MinecraftStream stream)
    {
        this.Message = stream.ReadString();
        this.Timestamp = stream.ReadDateTimeOffset();
        this.Salt = stream.ReadLong();
        this.Signature = stream.ReadUInt8Array();
        this.SignedPreview = stream.ReadBoolean();

        this.PreviouslySeenMessages = new List<SignedMessage>(stream.ReadVarInt());

        for (int i = 0; i < this.PreviouslySeenMessages.Count; i++)
            this.PreviouslySeenMessages[i] = stream.ReadSignedMessage();

        this.HasLastMessage = stream.ReadBoolean();
        if (this.HasLastMessage)
            this.LastMessage = stream.ReadSignedMessage();
        
    }
}

