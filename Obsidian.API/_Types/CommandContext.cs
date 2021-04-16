using Obsidian.API;
using Obsidian.API.Plugins;
using System;

namespace Obsidian.API
{
    public class CommandContext
    {
        internal string Message;

        public CommandContext(string message, IPlayer player, IServer server, CommandIssuer issuer)
        {
            this.Player = player;
            this.Server = server;
            this.Issuer = issuer;
            this.Message = message;
        }

        public IPlayer Player { get; private set; }

        public IServer Server { get; private set; }
        public CommandIssuer Issuer { get; }
        public PluginBase Plugin { get; internal set; }
    }
}