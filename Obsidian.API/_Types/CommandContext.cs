using Obsidian.API;
using System;

namespace Obsidian.API
{
    public class CommandContext
    {
        internal string Message;

        public CommandContext(string message, IPlayer player, IServer server/*, IClient client*/)
        {
            this.Player = player;
            this.Server = server;
            this.Message = message;
            //this.Client = client;
        }

        public IPlayer Player { get; private set; }

        public IServer Server { get; private set; }

        public CommandDependencyBundle Dependencies
        {
            get
            {
                return _depencendies;
            }
            set
            {
                if (_depencendies != null)
                    throw new Exception("Dependencies were already set for this context!");
                _depencendies = value;
            }
        }
        private CommandDependencyBundle _depencendies;

        // public IClient Client { get; private set; }
    }
}