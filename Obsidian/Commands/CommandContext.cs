using Obsidian.Connection;
using Qmmands;
using System;
using System.Collections.Generic;
using System.Text;
using Obsidian.Entities;

namespace Obsidian.Commands
{
    public sealed class CommandContext : ICommandContext
    {
        public Client Client { get; }
        public MinecraftPlayer Player => Client.Player;
        public Server Server { get; }
        public CommandContext(Client client, Server server)
        {
            this.Client = client;
            this.Server = server;
        }
    }
}
