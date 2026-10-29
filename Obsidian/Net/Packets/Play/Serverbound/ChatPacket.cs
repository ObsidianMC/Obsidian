using Obsidian.API.Events;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatPacket
{
    public ChatMessageTypes.ChatAcknowledgement Acknowledgement { get; private set; }
    [Field(0)]
    public string Message { get; private set; } = default!;

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(4)]
    public ReadOnlyMemory<byte> Signature { get; private set; }

    [Field(5)]
    public bool SignedPreview { get; set; }

    [Field(6)]
    public List<SignedMessage> LastSeenMessages { get; private set; } = default!;

    //TODO specify custom format in config
    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        await server.EventDispatcher.ExecuteEventAsync(new IncomingChatMessageEventArgs(player, server, this.Message, "<{0}> {1}"));
    }

    public override void Populate(INetStreamReader reader)
    {
        this.Message = reader.ReadString(256);
        this.Timestamp = reader.ReadDateTimeOffset();
        this.Salt = reader.ReadLong();

        var isSigned = reader.ReadBoolean();
        this.Signature = isSigned ? reader.ReadUInt8Array(256) : ReadOnlyMemory<byte>.Empty;
        this.Acknowledgement = ChatMessageTypes.ChatAcknowledgement.Read(reader);
    }
}

