using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatPacket
{
    [Field(0)]
    public string Message { get; private set; } = default!;

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(4)]
    public byte[] Signature { get; private set; } = default!;

    [Field(5)]
    public bool SignedPreview { get; set; }

    [Field(6)]
    public List<SignedMessage> LastSeenMessages { get; private set; } = default!;

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await server.HandleIncomingMessageAsync(this, player.client);
    }

    public override void Populate(INetStreamReader reader)
    {
        this.Message = reader.ReadString();
        this.Timestamp = reader.ReadDateTimeOffset();
        this.Salt = reader.ReadLong();
        
        var isSigned = reader.ReadBoolean();
        if (isSigned)
            this.Signature = reader.ReadUInt8Array(256);

        //var lastSeenMessagesLength = reader.ReadVarInt();
        //this.LastSeenMessages = new List<SignedMessage>(lastSeenMessagesLength);

        //for (int i = 0; i < lastSeenMessagesLength; i++)
        //    this.LastSeenMessages[i] = reader.ReadSignedMessage();
    }
}

