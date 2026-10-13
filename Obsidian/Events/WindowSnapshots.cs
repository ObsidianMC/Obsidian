using System.Threading;

namespace Obsidian.Events;

/// <summary>
/// What a player's open window showed, captured inside its lock to be sent after it: the packets, the window's state
/// they show (see <see cref="ClickWindowState"/>) and the order it was captured in among the player's snapshots.
/// </summary>
internal sealed record WindowSnapshot(
    IPlayer Player,
    long Sequence,
    ClickWindowState State,
    IReadOnlyList<IClientboundPacket> Packets);

/// <summary>
/// Keeps what a player is sent about their open window in order. Each snapshot is numbered as it's captured; one
/// captured before a snapshot the player was already sent, or showing a window that changed since, isn't sent, so
/// that it can't overwrite newer contents on the client.
/// </summary>
/// <remarks>
/// Whether a snapshot is still current is checked after plugins' packet handlers ran for it, and it's queued in the
/// same step, under a lock that changes made outside clicks queue their own packets under too (see
/// <see cref="Queue"/>). Such a change, made before the check, fails it; made after, its packets follow the snapshot.
/// </remarks>
internal sealed class WindowSnapshots
{
    private readonly Lock gate = new();

    private long captured;

    private long sent;

    /// <summary>
    /// Numbers a snapshot as it's captured, inside the window's lock.
    /// </summary>
    public long Next() => Interlocked.Increment(ref this.captured);

    /// <summary>
    /// Sends <paramref name="snapshot"/>, through plugins' packet handlers, unless a later one was sent already or
    /// the player's window changed since it was captured.
    /// </summary>
    /// <returns>Whether it wasn't sent because the window changed, so that a newer snapshot is needed.</returns>
    public async ValueTask<bool> SendAsync(WindowSnapshot snapshot)
    {
        if (snapshot.Sequence <= Volatile.Read(ref this.sent))
            return false;

        var packets = new List<IClientboundPacket>(snapshot.Packets.Count);
        foreach (var packet in snapshot.Packets)
        {
            if (await RunHandlersAsync(snapshot.Player, packet))
                packets.Add(packet);
        }

        lock (this.gate)
        {
            if (snapshot.Sequence <= this.sent)
                return false;

            if (!snapshot.State.IsCurrent(snapshot.Player))
                return true;

            this.sent = snapshot.Sequence;

            foreach (var packet in packets)
                QueueWithoutHandlers(snapshot.Player, packet);

            return false;
        }
    }

    /// <summary>
    /// Captures what <paramref name="player"/>'s window shows and queues it in one step, so that it's current when
    /// queued, without plugins' packet handlers, which can't run in between. Call it inside the window's lock, for a
    /// window that keeps changing while its snapshots go through the handlers.
    /// </summary>
    public void SendNow(IPlayer player, Func<IReadOnlyList<IClientboundPacket>> capture)
    {
        lock (this.gate)
        {
            this.sent = this.Next();

            foreach (var packet in capture())
                QueueWithoutHandlers(player, packet);
        }
    }

    /// <summary>
    /// Queues <paramref name="packet"/>, which shows a change made to <paramref name="player"/>'s inventory outside a
    /// click (an item picked up, say), in order with their window's snapshots. Call it once the change is made: a
    /// snapshot checked before the change is queued ahead of the packet, and one checked after it fails the check and
    /// is captured again.
    /// </summary>
    public void Queue(IPlayer player, IClientboundPacket packet)
    {
        lock (this.gate)
            QueueWithoutHandlers(player, packet);
    }

    private static ValueTask<bool> RunHandlersAsync(IPlayer player, IClientboundPacket packet) =>
        player is IClientPlayer { Client: Client client }
            ? client.RunQueueHandlersAsync(packet)
            : ValueTask.FromResult(true);

    // Behind what's queued already, so that it keeps its place among the player's packets. A connection that isn't a
    // server's (a test's, say) has no queue, so it's sent.
    private static void QueueWithoutHandlers(IPlayer player, IClientboundPacket packet)
    {
        if (player is not IClientPlayer { Client: IClient connection })
            return;

        if (connection is Client client)
            client.QueueWithoutHandlers(packet);
        else
            connection.SendPacket(packet);
    }
}
