using Obsidian.API.Events;
using Obsidian.Net.Packets;

namespace Obsidian.Events.EventArgs;

public class QueuePacketEventArgs : BasePacketEventArgs, ICancellable
{
    public static new string Name => "QueuePacket";

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    internal QueuePacketEventArgs(IServer server, Client client, IPacket packet) : base(server, client, packet)
    {
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
