using Obsidian.API.Events;

namespace Obsidian.Events.EventArgs;

public class BasePacketEventArgs : BaseMinecraftEventArgs
{
    /// <summary>
    /// The client that invoked the event.
    /// </summary>
    public Client Client { get; set; }

    /// <summary>
    /// The packet being used to invoke this event.
    /// </summary>
    public IPacket Packet { get; private set; }

    internal BasePacketEventArgs(Server server, Client client, IPacket packet) : base(server)
    {
        Client = client;
        Packet = packet;
    }
}
