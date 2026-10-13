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
/// Keeps what a player is sent about their open window in order and current. Each snapshot is numbered as it's
/// captured; one captured before a snapshot the player was already sent, or showing a window that changed since, isn't
/// sent, so that it can't overwrite newer contents on the client.
/// </summary>
/// <remarks>
/// <para>
/// Every packet goes through plugins' packet handlers first, outside any lock. Whether it's still current is checked
/// after them, and it's queued in the same step, under this object's lock, which changes made outside clicks are
/// checked and queued under too (see <see cref="SendChangeAsync"/>).
/// </para>
/// <para>
/// Whatever wasn't sent because the window changed leaves the window <see cref="IsStale"/>: what the client shows
/// may be behind. The next whole snapshot sent brings it up to date, whether a click's or the one the player's tick
/// sends while the window is stale. That's tried once a tick, so a window that keeps changing is sent again as soon as
/// it holds still long enough, never in a loop.
/// </para>
/// </remarks>
/// <param name="runHandlers">
/// Runs plugins' packet handlers for queueing a packet to the player; returns whether it's still to be queued.
/// </param>
/// <param name="queue">Queues a packet to the player, behind what's queued already, without the handlers.</param>
internal sealed class WindowSnapshots(
    Func<IClientboundPacket, ValueTask<bool>> runHandlers,
    Action<IClientboundPacket> queue)
{
    private readonly Lock gate = new();

    private long captured;

    private long sent;

    private bool stale;

    /// <summary>
    /// Whether something about the player's window wasn't sent because the window changed before it could be, so that
    /// a whole snapshot is due.
    /// </summary>
    public bool IsStale
    {
        get
        {
            lock (this.gate)
                return this.stale;
        }
    }

    /// <summary>
    /// Numbers a snapshot as it's captured, inside the window's lock.
    /// </summary>
    public long Next() => Interlocked.Increment(ref this.captured);

    /// <summary>
    /// Sends <paramref name="snapshot"/>, which shows the player's whole window, through plugins' packet handlers,
    /// unless a later one was sent already or the window changed since it was captured.
    /// </summary>
    /// <returns>Whether it wasn't sent because the window changed, so that a newer snapshot is needed.</returns>
    public async ValueTask<bool> SendAsync(WindowSnapshot snapshot)
    {
        if (snapshot.Sequence <= Volatile.Read(ref this.sent))
            return false;

        var packets = await this.RunHandlersAsync(snapshot.Packets);

        lock (this.gate)
        {
            if (snapshot.Sequence <= this.sent)
                return false;

            if (!snapshot.State.IsCurrent(snapshot.Player))
            {
                this.stale = true;
                return true;
            }

            // Current as it's queued, it brings the client up to date, whatever was left out before it.
            this.sent = snapshot.Sequence;
            this.stale = false;

            foreach (var packet in packets)
                queue(packet);

            return false;
        }
    }

    /// <summary>
    /// Sends <paramref name="packet"/>, which shows a change made to <paramref name="player"/>'s inventory outside a
    /// click (an item picked up, say), through plugins' packet handlers, unless the window changed since
    /// <paramref name="state"/>, taken right after the change.
    /// </summary>
    /// <returns>
    /// Whether it wasn't sent because the window changed; the window is then <see cref="IsStale"/>, and sent whole
    /// later.
    /// </returns>
    /// <remarks>
    /// It doesn't take a later whole snapshot's place, which may show more than the change, so it isn't numbered: a
    /// snapshot checked before the change is queued ahead of it, and one checked after it shows the change too.
    /// </remarks>
    public async ValueTask<bool> SendChangeAsync(IPlayer player, ClickWindowState state, IClientboundPacket packet)
    {
        var packets = await this.RunHandlersAsync([packet]);

        lock (this.gate)
        {
            if (!state.IsCurrent(player))
            {
                this.stale = true;
                return true;
            }

            foreach (var approved in packets)
                queue(approved);

            return false;
        }
    }

    // The packets no handler cancelled, in their order.
    private async ValueTask<List<IClientboundPacket>> RunHandlersAsync(IReadOnlyList<IClientboundPacket> packets)
    {
        var approved = new List<IClientboundPacket>(packets.Count);

        foreach (var packet in packets)
        {
            if (await runHandlers(packet))
                approved.Add(packet);
        }

        return approved;
    }
}
