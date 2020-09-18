using Obsidian.Entities;
using Qmmands;
using System;

namespace Obsidian.Commands
{
    public sealed class ObsidianContext : CommandContext
    {
        internal Client Client { get; }
        public Player Player => Client.Player;
        public Server Server { get; }
        public ObsidianContext(Client client, Server server, IServiceProvider provider) : base(provider)
        {
            this.Client = client;
            this.Server = server;
        }
    }
}
