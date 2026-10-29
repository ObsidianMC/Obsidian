using Obsidian.API.Commands;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatCommandSignedPacket
{
    public ChatMessageTypes.ChatAcknowledgement Acknowledgement { get; private set; }
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
        if (argumentSignaturesLength is < 0 or > 8)
            throw new System.IO.InvalidDataException("Invalid signed argument count.");

        ArgumentSignatures = new List<ArgumentSignature>(argumentSignaturesLength);
        for (int i = 0; i < argumentSignaturesLength; i++)
            ArgumentSignatures.Add(reader.ReadArgumentSignature());

        this.Acknowledgement = ChatMessageTypes.ChatAcknowledgement.Read(reader);
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var context = new CommandContext($"/{this.Command}", new CommandSender(CommandIssuers.Client, player), player, server);

        await server.CommandHandler.ProcessCommand(context);
    }
}
