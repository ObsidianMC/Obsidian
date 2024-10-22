﻿using Microsoft.Extensions.Logging;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChatCommandPacket : IServerboundPacket
{
    [Field(0)]
    public string Command { get; private set; } = default!;

    public int Id => 0x04;

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
