using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs;

public class BasePacketEventArgs : AsyncEventArgs
{
    /// <summary>
    /// The client that invoked the event.
    /// </summary>
    public Client Client { get; set; }

    /// <summary>
    /// The packet being used to invoke this event.
    /// </summary>
    public IPacket Packet { get; private set; }

    internal BasePacketEventArgs(Client client, IPacket packet)
    {
        Client = client;
        Packet = packet;
    }
}
