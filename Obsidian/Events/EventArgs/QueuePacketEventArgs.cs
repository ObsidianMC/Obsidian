using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs;

public sealed class QueuePacketEventArgs : BasePacketEventArgs, ICancellable
{
    internal QueuePacketEventArgs(Client client, IPacket packet) : base(client, packet) { }
    public bool Cancelled { get; set; }
}
