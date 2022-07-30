using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs;

public class BasePacketEventArgs : BaseEventArgs
{
    internal BasePacketEventArgs(Client client, IPacket packet)
    {
        Client = client;
        Packet = packet;
    }

    /// <summary>
    /// The client that invoked the event.
    /// </summary>
    public Client Client { get; }

    /// <summary>
    /// The packet being used to invoke this event.
    /// </summary>
    public IPacket Packet { get; }
}
