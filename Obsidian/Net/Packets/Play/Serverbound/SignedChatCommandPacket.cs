using Microsoft.Extensions.Logging;
using Obsidian.Commands;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

//TODO finish full impl
public sealed partial class SignedChatCommandPacket : IServerboundPacket
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
    public bool SignedPreview { get; private set; }

    public int Id => 0x05;

    public ValueTask HandleAsync(Client client) => default;
    public async ValueTask HandleAsync(Server server, Player player)
    {
        var context = new CommandContext($"/{this.Command}", new CommandSender(CommandIssuers.Client, player, server._logger), player, server);
        try
        {
            await server.CommandsHandler.ProcessCommand(context);
        }
        catch (Exception e)
        {
            server._logger.LogError(e, e.Message);
        }
    }
}
