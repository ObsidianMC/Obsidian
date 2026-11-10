using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Obsidian's own work that a flow which mustn't wait for the gates (see
/// <see cref="AbstractLevel.MustNotWaitForGates"/>) asks for, run in the background once the ticks let go of them (see <see cref="Server.BackgroundWork"/>). Each is
/// tracked from the moment it's accepted, so the shutdown waits for those under way, and none starts once it closed.
/// A failure is logged.
/// </summary>
internal sealed partial class DeferredWork(ILogger logger)
{
    private readonly Lock gate = new();
    private readonly HashSet<Task> running = [];
    private bool closed;

    /// <summary>
    /// Starts <paramref name="work"/> in the background, apart from the caller's flow, so that it doesn't count as
    /// inside the caller's tick.
    /// </summary>
    /// <returns>Whether it started: not once closed, as the final save that closing leads to covers it.</returns>
    public bool TryStart(Func<Task> work)
    {
        lock (this.gate)
        {
            if (this.closed)
                return false;

            Task task;
            using (ExecutionContext.SuppressFlow())
                task = Task.Run(work);

            // What a drain waits for: set once the work ended and its failure, if any, was logged.
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.running.Add(finished.Task);
            _ = task.ContinueWith(
                ended => this.Finish(ended, finished),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            return true;
        }
    }

    /// <summary>Stops accepting work: what is asked for from now on isn't run.</summary>
    public void Close()
    {
        lock (this.gate)
            this.closed = true;
    }

    /// <summary>Waits for the work accepted so far to end, its failures logged, and keeps accepting work.</summary>
    public Task DrainAsync()
    {
        Task[] left;
        lock (this.gate)
            left = [.. this.running];

        return Task.WhenAll(left);
    }

    /// <summary>
    /// Closes (see <see cref="Close"/>) and waits for the work accepted before to end, its failures logged.
    /// </summary>
    public Task CloseAndDrainAsync()
    {
        Task[] left;
        lock (this.gate)
        {
            this.closed = true;
            left = [.. this.running];
        }

        return Task.WhenAll(left);
    }

    // The work ends for the drains however its failure's logging goes: a logger that throws (a full disk, say) mustn't
    // keep the shutdown waiting for good.
    private void Finish(Task task, TaskCompletionSource finished)
    {
        try
        {
            if (task.Exception is AggregateException failure)
                Log.DeferredWorkFailed(logger, failure);
        }
        catch (Exception)
        {
            // Nothing is left to report it with.
        }
        finally
        {
            lock (this.gate)
                this.running.Remove(finished.Task);

            finished.TrySetResult();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Work a tick left to run in the background failed")]
        public static partial void DeferredWorkFailed(ILogger logger, Exception exception);
    }
}
