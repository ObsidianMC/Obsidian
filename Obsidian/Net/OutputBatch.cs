using System.Threading;

namespace Obsidian.Net;

/// <summary>
/// Holds back connections' flushes while a world tick runs, then posts each connection's flush once when it ends, like
/// vanilla, which flushes its connections once a tick. Without it, every packet a tick sends (a mob's movement to each
/// player in range, say) posted its own flush to the network threads, and the tick spent much of its time contending
/// with them for the posting queue's and the connections' locks.
/// </summary>
/// <remarks>
/// The batch flows with the tick's execution context, into the levels it ticks in parallel. Output sent outside a
/// tick (packet handlers, the chunk loop), or after the batch ended by work the tick started, is flushed at once.
/// </remarks>
internal sealed class OutputBatch : IDisposable
{
    private static readonly AsyncLocal<OutputBatch?> current = new();

    private readonly Lock gate = new();
    private List<Action>? flushes = [];

    private OutputBatch()
    {
    }

    /// <summary>
    /// Starts holding back flushes in the calling flow and the work it awaits, until the batch is disposed. Call it in
    /// the async method whose awaits should be covered: the batch doesn't flow back to its caller.
    /// </summary>
    public static OutputBatch Begin() => current.Value = new OutputBatch();

    /// <summary>Whether a batch holds back flushes in this flow.</summary>
    public static bool InBatch => current.Value is OutputBatch { flushes: not null };

    /// <summary>
    /// Has the running batch post <paramref name="flush"/> once it ends.
    /// </summary>
    /// <returns>Whether it was deferred: false when no batch runs in this flow, or it ended already.</returns>
    public static bool TryDefer(Action flush)
    {
        if (current.Value is not OutputBatch batch)
            return false;

        lock (batch.gate)
        {
            if (batch.flushes is null)
                return false;

            batch.flushes.Add(flush);
            return true;
        }
    }

    /// <summary>Posts the deferred flushes to the network threads; flushes asked for from now on happen at once.</summary>
    public void Dispose()
    {
        List<Action>? flushes;
        lock (this.gate)
        {
            flushes = this.flushes;
            this.flushes = null;
        }

        if (ReferenceEquals(current.Value, this))
            current.Value = null;

        if (flushes is null)
            return;

        foreach (var flush in flushes)
            WorkerThreads.Network.Post(flush);
    }
}
