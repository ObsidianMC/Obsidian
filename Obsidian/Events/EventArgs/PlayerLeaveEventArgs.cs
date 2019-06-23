using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class PlayerLeaveEventArgs : BaseMinecraftEventArgs
    {
        public Player WhoLeft { get;  }
        internal PlayerLeaveEventArgs(Client client) : base(client)
        {
            this.WhoLeft = client.Player;
        }
    }
}
