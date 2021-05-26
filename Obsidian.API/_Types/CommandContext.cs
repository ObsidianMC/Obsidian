using Obsidian.API.Plugins;
using System;

namespace Obsidian.API
{
    public class CommandContext
    {
        public IPlayer Player { get; private set; }
        public IServer Server { get; private set; }
        public ICommandSender Sender { get; }
        public PluginBase Plugin { get; internal set; }
        internal ReadOnlyMemory<char> Message { get; }

        public CommandContext(string message, ICommandSender commandSender, IPlayer player, IServer server) : this(message.AsMemory(), commandSender, player, server)
        {
        }

        public CommandContext(ReadOnlyMemory<char> message, ICommandSender commandSender, IPlayer player, IServer server)
        {
            Server = server;
            Sender = commandSender;
            Player = player;
            Message = message;
        }
    }
}