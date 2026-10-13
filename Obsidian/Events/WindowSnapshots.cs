using System.Threading;

namespace Obsidian.Events;

/// <summary>
/// What a player's open window showed, captured inside its lock to be sent after it: the packets, the window's state
/// they show (see <see cref="WindowState"/>) and the order it was captured in among the player's snapshots.
/// </summary>
internal sealed record WindowSnapshot(
    IPlayer Player,
    long Sequence,
    WindowState State,
    IReadOnlyList<IClientboundPacket> Packets);

/// <summary>
/// Keeps the window snapshots clicks send a player in order. Each is numbered as it's captured; one captured before a
/// snapshot the player was already sent, or showing a window that changed since, isn't sent, so that it can't
/// overwrite newer contents on the client.
/// </summary>
internal sealed class WindowSnapshots
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private long captured;

    private long sent;

    /// <summary>
    /// Numbers a snapshot as it's captured, inside the window's lock.
    /// </summary>
    public long Next() => Interlocked.Increment(ref this.captured);

    /// <summary>
    /// Sends <paramref name="snapshot"/> unless a later one was sent already or, unless <paramref name="force"/>, the
    /// player's window changed since it was captured.
    /// </summary>
    /// <returns>Whether it wasn't sent because the window changed, so that a newer snapshot is needed.</returns>
    public async ValueTask<bool> SendAsync(WindowSnapshot snapshot, bool force)
    {
        await this.gate.WaitAsync();
        try
        {
            if (snapshot.Sequence <= this.sent)
                return false;

            if (!force && !snapshot.State.IsCurrent(snapshot.Player))
                return true;

            this.sent = snapshot.Sequence;

            foreach (var packet in snapshot.Packets)
                await snapshot.Player.QueuePacketAsync(packet);

            return false;
        }
        finally
        {
            this.gate.Release();
        }
    }
}
