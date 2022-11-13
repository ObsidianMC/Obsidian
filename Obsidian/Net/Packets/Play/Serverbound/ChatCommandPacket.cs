using Microsoft.Extensions.Logging;
using Obsidian.Commands;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatCommandPacket : IServerboundPacket
{
    [Field(0)]
    public string Command { get; private set; }

    [Field(1)]
    public DateTimeOffset Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(3)]
    public List<ArgumentSignature> ArgumentSignatures { get; private set; }

    [Field(4)]
    public bool SignedPreview { get; private set; }

    public int Id => 0x04;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        var context = new CommandContext($"/{this.Command}", new CommandSender(CommandIssuers.Client, player, server.Logger), player, server);
        try
        {
            await server.CommandsHandler.ProcessCommand(context);
        }
        catch (Exception e)
        {
            server.Logger.LogError(e, e.Message);
        }
    }
}
