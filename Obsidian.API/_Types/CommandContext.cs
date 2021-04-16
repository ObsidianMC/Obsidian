using Obsidian.API;
using Obsidian.API.Plugins;
using System;

namespace Obsidian.API
{
    public class CommandContext
    {
        internal string Message;

        public CommandContext(string message, ICommandSender commandSender, IServer server)
        {
            this.Server = server;
            this.Sender = commandSender;
            this.Message = message;
        }

        public IPlayer Player { get; private set; }

        public IServer Server { get; private set; }
        public ICommandSender Sender { get; }
        public PluginBase Plugin { get; internal set; }
    }
}