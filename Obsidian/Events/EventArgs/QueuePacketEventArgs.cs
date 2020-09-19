using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs
{
    public class QueuePacketEventArgs : BasePacketEventArgs, ICancellable
    {
        public bool Cancel { get; set; }

        internal QueuePacketEventArgs(Client client, Packet packet) : base(client, packet) { }
    }
}
