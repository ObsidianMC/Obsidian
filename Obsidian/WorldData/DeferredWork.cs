using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Work asked for from a flow that mustn't wait for the gates (see <see cref="AbstractLevel.MustNotWaitForGates"/>),
/// run in the background once the ticks let go of them. Each is tracked from the moment it's accepted, so a shutdown or
/// disposal waits for those under way, and none starts once it closed.
/// </summary>
/// <remarks>
/// Work for a target (a level's save, a player's save) is coalesced: asked for again while the target's is still
/// running, it runs once more after it, however many times it was asked for meanwhile. A plugin saving every tick so
/// keeps at most one save running and one waiting per target, and every request is followed by a run that started
/// after it.
/// </remarks>
internal sealed partial class DeferredWork(ILogger logger, bool keepsFailures = false)
{
    private readonly Lock gate = new();
    private readonly HashSet<Task> running = [];
    private readonly Dictionary<object, Coalesced> byTarget = [];
    private readonly List<Exception> failures = [];
    private bool closed;

    /// <summary>
    /// Starts <paramref name="work"/> in the background, apart from the caller's flow, so that it doesn't count as
    /// inside the caller's tick. Its failure is logged, and kept for <see cref="TakeFailures"/>.
    /// </summary>
    /// <param name="work">What to run.</param>
    /// <param name="alsoTrackedBy">
    /// Another tracker that must accept it too, atomically with this one: a player's or a dimension's saves are their
    /// world's too. This one's lock comes first, so the world's tracker is always the one passed here.
    /// </param>
    /// <param name="target">
    /// What the work is for, to coalesce it with work for the same target still running (see the remarks); none for
    /// work that must run each time it's asked for.
    /// </param>
    /// <returns>The work, or <c>null</c> once either closed: the final save that closing leads to covers it.</returns>
    public Task? TryStart(Func<Task> work, DeferredWork? alsoTrackedBy = null, object? target = null)
    {
        lock (this.gate)
        {
            if (this.closed)
                return null;

            if (target is not null && this.byTarget.TryGetValue(target, out var queued))
            {
                queued.Again = true;
                return queued.Task;
            }

            if (alsoTrackedBy is null)
                return this.StartUnderLock(work, target);

            lock (alsoTrackedBy.gate)
            {
                if (alsoTrackedBy.closed)
                    return null;

                var task = this.StartUnderLock(work, target);
                alsoTrackedBy.TrackUnderLock(task, logs: false);
                return task;
            }
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

    /// <summary>
    /// The failures of the work that ended since the last call, which a save of everything reports as its own.
    /// </summary>
    public Exception[] TakeFailures()
    {
        lock (this.gate)
        {
            Exception[] taken = [.. this.failures];
            this.failures.Clear();
            return taken;
        }
    }

    // Called under gate. Work for a target runs again while it was asked for again during its run.
    private Task StartUnderLock(Func<Task> work, object? target)
    {
        Coalesced? coalesced = null;
        if (target is not null)
        {
            coalesced = new Coalesced();
            this.byTarget.Add(target, coalesced);
        }

        Func<Task> run = coalesced is null ? work : () => this.RunCoalescedAsync(work, target!, coalesced);
        Task task;
        using (ExecutionContext.SuppressFlow())
            task = Task.Run(run);

        if (coalesced is not null)
            coalesced.Task = task;

        this.TrackUnderLock(task);
        return task;
    }

    private async Task RunCoalescedAsync(Func<Task> work, object target, Coalesced coalesced)
    {
        try
        {
            while (true)
            {
                lock (this.gate)
                    coalesced.Again = false;

                await work();

                lock (this.gate)
                {
                    if (!coalesced.Again)
                        return;
                }
            }
        }
        finally
        {
            lock (this.gate)
                this.byTarget.Remove(target);
        }
    }

    // Called under gate. Work two trackers share is logged by the one that started it.
    private void TrackUnderLock(Task task, bool logs = true)
    {
        this.running.Add(task);
        _ = task.ContinueWith(
            ended => this.Finish(ended, logs),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void Finish(Task task, bool logs)
    {
        lock (this.gate)
        {
            this.running.Remove(task);
            if (keepsFailures && task.Exception is AggregateException failure)
                this.failures.Add(failure);
        }

        if (logs && task.Exception is AggregateException logged)
            Log.DeferredWorkFailed(logger, logged);
    }

    // Work for a target, and whether it was asked for again since its run started. Guarded by gate.
    private sealed class Coalesced
    {
        public Task Task { get; set; } = Task.CompletedTask;

        public bool Again { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Work a tick left to run in the background failed")]
        public static partial void DeferredWorkFailed(ILogger logger, Exception exception);
    }
}
