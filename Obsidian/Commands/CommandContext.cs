using Obsidian.Entities;
using Qmmands;

namespace Obsidian.Commands
{
    public sealed class CommandContext : ICommandContext
    {
        public Client Client { get; }
        public Player Player => Client.Player;
        public Server Server { get; }
        public CommandContext(Client client, Server server)
        {
            this.Client = client;
            this.Server = server;
        }
    }
}
