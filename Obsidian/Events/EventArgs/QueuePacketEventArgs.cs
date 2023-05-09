using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs;

public class QueuePacketEventArgs : BasePacketEventArgs, ICancellable
{
    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    internal QueuePacketEventArgs(Client client, IPacket packet) : base(client, packet) { }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
