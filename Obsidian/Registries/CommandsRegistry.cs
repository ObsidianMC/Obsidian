using Obsidian.API.Commands;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Registries;

/// <summary>
/// Sends players the command tree their client completes and validates commands with. Like vanilla, each player gets
/// a tree of only the commands they may run, so it is resent whenever that may change.
/// </summary>
public static class CommandsRegistry
{
    /// <summary>Builds the commands packet for <paramref name="player"/>.</summary>
    public static async Task<CommandsPacket> CreatePacketAsync(IServer server, IPlayer player)
    {
        var handler = (CommandHandler)server.CommandHandler;
        var context = new CommandContext(string.Empty, new CommandSender(CommandIssuers.Client, player), player, server);

        var packet = new CommandsPacket();
        packet.AddNode(await handler.BuildCommandTreeAsync(context));

        return packet;
    }

    /// <summary>Sends <paramref name="player"/> the commands they may currently run.</summary>
    public static async Task SendAsync(IServer server, IPlayer player) =>
        await player.QueuePacketAsync(await CreatePacketAsync(server, player));

    /// <summary>Resends the command tree to every online player, after commands were registered or removed.</summary>
    public static async Task RefreshAsync(IServer server)
    {
        foreach (var player in server.OnlinePlayers.Values)
        {
            try
            {
                await SendAsync(server, player);
            }
            catch (OperationCanceledException)
            {
                // The player disconnected meanwhile.
            }
        }
    }
}
