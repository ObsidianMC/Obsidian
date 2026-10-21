using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.Net;

/// <summary>
/// The threads connections' output is written on. Like vanilla's network threads, they take the work of getting a packet
/// onto the wire (compressing, encrypting and sending it) off whoever sent it, so a tick, the chunk loop or a broadcast
/// only serializes its packets. They're dedicated rather than the thread pool's, so busy ticks or plugins don't delay
/// players' output either.
/// </summary>
/// <remarks>
/// Work posted here must not throw or block: every connection's output shares these threads.
/// </remarks>
internal static class NetworkThreads
{
    private static readonly BlockingCollection<Action> queue = [];

    static NetworkThreads()
    {
        // Mostly compression and the per-byte cipher: a few threads keep up with many players.
        var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        for (var i = 0; i < threadCount; i++)
        {
            new Thread(Work)
            {
                IsBackground = true,
                Name = $"Network {i + 1}"
            }.Start();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on one of the network threads.
    /// </summary>
    public static void Post(Action work) => queue.Add(work);

    private static void Work()
    {
        foreach (var work in queue.GetConsumingEnumerable())
            work();
    }
}
