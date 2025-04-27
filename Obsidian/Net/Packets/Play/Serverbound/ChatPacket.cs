using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using OpenTelemetry.Resources;
using static Org.BouncyCastle.Bcpg.Attr.ImageAttrib;

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

    //TODO specify custom format in config
    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        await server.EventDispatcher.ExecuteEventAsync(new IncomingChatMessageEventArgs(player, server, this.Message, "<{0}> {1}"));
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

