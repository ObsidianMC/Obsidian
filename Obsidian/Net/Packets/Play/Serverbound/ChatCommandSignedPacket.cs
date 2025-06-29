using Obsidian.API.Commands;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

//TODO finish full impl
public partial class ChatCommandSignedPacket
{
    [Field(0)]
    public string Command { get; private set; } = default!;

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(3)]
    public List<ArgumentSignature> ArgumentSignatures { get; private set; } = default!;

    [Field(4)]
    public List<SignedMessage> LastSeenMessages { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        Command = reader.ReadString();
        Timestamp = reader.ReadDateTimeOffset();
        Salt = reader.ReadLong();

        var argumentSignaturesLength = reader.ReadVarInt();

        ArgumentSignatures = new List<ArgumentSignature>(argumentSignaturesLength);
        for (int i = 0; i < argumentSignaturesLength; i++)
            ArgumentSignatures[i] = reader.ReadArgumentSignature();

        var lastSeenMessagesLength = reader.ReadVarInt();

        //LastSeenMessages = new List<SignedMessage>(lastSeenMessagesLength);
        //for(int i = 0;i < lastSeenMessagesLength; i++)
        //    reader.ReadUInt8Array(256);//There's still a lot to this I don't understand so maybe someone can 😭😭
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var context = new CommandContext($"/{this.Command}", new CommandSender(CommandIssuers.Client, player), player, server);

        await server.CommandHandler.ProcessCommand(context);
    }
}
