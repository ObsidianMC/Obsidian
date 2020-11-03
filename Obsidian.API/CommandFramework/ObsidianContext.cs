using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API
{
    public class ObsidianContext : BaseCommandContext
    {

        public ObsidianContext(string message, IPlayer player, IServer server/*, IClient client*/) : base(message)
        {
            this.Player = player;
            this.Server = server;
            //this.Client = client;
        }

        public IPlayer Player { get; private set; }

        public IServer Server { get; private set; }

        // public IClient Client { get; private set; }
    }
}
