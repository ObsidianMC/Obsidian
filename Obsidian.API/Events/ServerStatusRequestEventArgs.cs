using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API.Events
{
    public class ServerStatusRequestEventArgs : BaseMinecraftEventArgs
    {
        public IServerStatus Status { get; }
        internal ServerStatusRequestEventArgs(IServer server, IServerStatus status) : base(server)
        {
            this.Status = status;
        }
    }
}
