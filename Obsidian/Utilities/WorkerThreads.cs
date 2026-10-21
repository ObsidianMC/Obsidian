using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Obsidian.Utilities;

/// <summary>
/// Dedicated threads that run work posted to them, for work that shouldn't take or wait for the thread pool, which
/// the levels' ticks, packet handlers and plugins run on: a busy pool doesn't delay it, and it doesn't hold pool threads.
/// </summary>
/// <remarks>
/// Work posted here must not block waiting for other work on the same threads, and should handle and log its own
/// failures.
/// </remarks>
internal sealed class WorkerThreads
{
    /// <summary>
    /// Where connections' output is written, like vanilla's network threads: compressing, encrypting and sending happen
    /// here rather than on whoever sent the packet, so a tick, the chunk loop or a broadcast only serializes it. Mostly
    /// compression, so a few threads keep up with many players. Work here must not block.
    /// </summary>
    public static WorkerThreads Network { get; } = new("Network", Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    /// <summary>
    /// Where blocking file work runs, like vanilla's IO workers: flushing region files to disk waits on the disk, which
    /// would otherwise hold a pool thread for every region an autosave flushes.
    /// </summary>
    public static WorkerThreads Storage { get; } = new("Storage", 8);

    private readonly BlockingCollection<Action> queue = [];

    private WorkerThreads(string name, int threadCount)
    {
        for (var i = 0; i < threadCount; i++)
        {
            new Thread(this.Work)
            {
                IsBackground = true,
                Name = $"{name} {i + 1}"
            }.Start();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on one of the threads. It should handle its own failures (see <see cref="RunAsync"/>
    /// for work whose caller does).
    /// </summary>
    public void Post(Action work) => this.queue.Add(work);

    /// <summary>
    /// Runs <paramref name="work"/> on one of the threads.
    /// </summary>
    /// <returns>A task that completes once it ran, or fails with what it threw. Its continuations don't run here.</returns>
    public Task RunAsync(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.Post(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });

        return done.Task;
    }

    private void Work()
    {
        foreach (var work in this.queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                // A last resort, for work that let a failure escape: it mustn't end a thread the other work shares,
                // which would crash the server.
                Trace.TraceError($"{Thread.CurrentThread.Name}: work failed: {exception}");
            }
        }
    }
}
