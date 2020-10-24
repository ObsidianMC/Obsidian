using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
using System;

namespace Obsidian.Commands
{
    public sealed class ObsidianContext : BaseCommandContext
    {
        internal Client Client { get; }
        public Player Player => Client.Player;
        public Server Server { get; }

        public ObsidianContext(string message, Client client, Server server) : base(message)
        {
            this.Client = client;
            this.Server = server;
        }
    }
}
