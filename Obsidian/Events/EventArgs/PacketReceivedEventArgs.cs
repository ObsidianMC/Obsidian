using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs
{
    public class PacketReceivedEventArgs : BasePacketEventArgs
    {
        internal PacketReceivedEventArgs(Client client, Packet receivedPacket) : base(client, receivedPacket) { }
    }
}
