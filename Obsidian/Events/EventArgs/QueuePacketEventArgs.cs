using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs
{
    public class QueuePacketEventArgs : BasePacketEventArgs, ICancellable
    {
        public bool Cancel { get; set; }

        internal QueuePacketEventArgs(Client client, IPacket packet) : base(client, packet) { }
    }
}
