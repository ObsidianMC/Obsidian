using Obsidian.API.Commands;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatCommandPacket
{
    [Field(0)]
    public string Command { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        Command = reader.ReadString();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var context = new CommandContext($"/{this.Command}", new CommandSender(CommandIssuers.Client, player), player, server);

        await server.CommandHandler.ProcessCommand(context);
    }
}
