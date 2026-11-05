using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Work asked for from a flow that mustn't wait for the gates (see <see cref="AbstractLevel.MustNotWaitForGates"/>),
/// run in the background once the ticks let go of them. Each is tracked from the moment it's accepted, so a shutdown or
/// disposal waits for those under way, and none starts once it closed.
/// </summary>
internal sealed partial class DeferredWork(ILogger logger)
{
    private readonly Lock gate = new();
    private readonly HashSet<Task> running = [];
    private bool closed;

    /// <summary>
    /// Starts <paramref name="work"/> in the background, apart from the caller's flow, so that it doesn't count as
    /// inside the caller's tick. Its failure is logged.
    /// </summary>
    /// <returns>The work, or <c>null</c> once closed: the final save that closing leads to covers it.</returns>
    public Task? TryStart(Func<Task> work)
    {
        lock (this.gate)
        {
            if (this.closed)
                return null;

            Task task;
            using (ExecutionContext.SuppressFlow())
                task = Task.Run(work);

            this.running.Add(task);
            _ = task.ContinueWith(
                this.Finish,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>Stops accepting work: what is asked for from now on isn't run.</summary>
    public void Close()
    {
        lock (this.gate)
            this.closed = true;
    }

    /// <summary>Waits for the work accepted so far, without closing. Failures were logged already.</summary>
    public Task WhenIdleAsync()
    {
        Task[] left;
        lock (this.gate)
            left = [.. this.running];

        return Task.WhenAll(left).ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>Closes (see <see cref="Close"/>) and waits for the work accepted before.</summary>
    public Task CloseAndDrainAsync()
    {
        this.Close();
        return this.WhenIdleAsync();
    }

    private void Finish(Task task)
    {
        lock (this.gate)
            this.running.Remove(task);

        if (task.Exception is AggregateException failure)
            Log.DeferredWorkFailed(logger, failure);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Work a tick left to run in the background failed")]
        public static partial void DeferredWorkFailed(ILogger logger, Exception exception);
    }
}
