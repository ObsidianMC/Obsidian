using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs
{
    public class PacketReceivedEventArgs : BasePacketEventArgs
    {
        internal PacketReceivedEventArgs(Client client, IPacket receivedPacket) : base(client, receivedPacket) { }
    }
}
